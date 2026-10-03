import { useEffect, useRef, useState } from "react";
import { ActivityIndicator, Alert, Button, SafeAreaView, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import * as ImagePicker from "expo-image-picker";
import { File, Paths } from "expo-file-system";
import * as Sharing from "expo-sharing";
import * as SecureStore from "expo-secure-store";

type Page<T> = { items: T[] };
type Zev = { id: string; code: string; name: string };
type Card = { personId: string; balance: number; charges: { id: string; period: string; amount: number; open: number; reference: string }[];
  payments: { id: string; paidOn: string; amount: number }[] };
type Ticket = { id: string; category: string; description: string; status: string };
type Announcement = { id: string; title: string; content: string };
type Document = { id: string; title: string; type: string };
type Ballot = { id: string; title: string; question: string; status: string };
type Unit = { id: string; zevId: string; code: string };
type MobileTokens = { accessToken: string; refreshToken: string };
type MobileProfile = { email: string; mustChangePassword: boolean };

const apiBase = process.env.EXPO_PUBLIC_API_URL;

export default function App() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [mustChangePassword, setMustChangePassword] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [token, setToken] = useState("");
  const [zevs, setZevs] = useState<Zev[]>([]);
  const [zevId, setZevId] = useState("");
  const [cards, setCards] = useState<Card[]>([]);
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [announcements, setAnnouncements] = useState<Announcement[]>([]);
  const [documents, setDocuments] = useState<Document[]>([]);
  const [ballots, setBallots] = useState<Ballot[]>([]);
  const [units, setUnits] = useState<Unit[]>([]);
  const [selectedUnitId, setSelectedUnitId] = useState("");
  const [category, setCategory] = useState("");
  const [description, setDescription] = useState("");
  const [location, setLocation] = useState("");
  const [photo, setPhoto] = useState<ImagePicker.ImagePickerAsset | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const refreshInFlight = useRef<Promise<string | null> | null>(null);

  async function renewAccessToken(): Promise<string | null> {
    if (refreshInFlight.current) return refreshInFlight.current;
    refreshInFlight.current = (async () => {
      const refreshToken = await SecureStore.getItemAsync("zev-refresh");
      if (!refreshToken || !apiBase) return null;
      const response = await fetch(`${apiBase}/api/auth/mobile/refresh`, { method: "POST",
        headers: { "Content-Type": "application/json" }, body: JSON.stringify({ refreshToken }) });
      if (!response.ok) { await SecureStore.deleteItemAsync("zev-refresh"); return null; }
      const next = await response.json() as MobileTokens;
      await SecureStore.setItemAsync("zev-refresh", next.refreshToken);
      setToken(next.accessToken);
      return next.accessToken;
    })().finally(() => { refreshInFlight.current = null; });
    return refreshInFlight.current;
  }

  async function request<T>(path: string, accessToken: string, body?: object): Promise<T> {
    if (!apiBase?.startsWith("https://") && !__DEV__) throw new Error("Produkcijski API mora koristiti HTTPS.");
    if (!apiBase) throw new Error("Postavite EXPO_PUBLIC_API_URL na adresu API-ja dostupnu telefonu.");
    const headers = { ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...(body ? { "Content-Type": "application/json" } : {}) };
    let response = await fetch(`${apiBase}${path}`, { method: body ? "POST" : "GET",
      headers, body: body ? JSON.stringify(body) : undefined });
    if (response.status === 401 && accessToken) {
      const nextToken = await renewAccessToken();
      if (nextToken) response = await fetch(`${apiBase}${path}`, { method: body ? "POST" : "GET",
        headers: { ...headers, Authorization: `Bearer ${nextToken}` },
        body: body ? JSON.stringify(body) : undefined });
    }
    if (!response.ok) {
      const problem = await response.json().catch(() => null) as { error?: string } | null;
      throw new Error(problem?.error ?? `API greška (${response.status})`);
    }
    if (response.status === 204) return undefined as T;
    return response.json() as Promise<T>;
  }

  async function login() {
    setBusy(true); setError("");
    try {
      const result = await request<MobileTokens>("/api/auth/mobile/login", "", { email, password });
      await SecureStore.setItemAsync("zev-refresh", result.refreshToken);
      const profile = await request<MobileProfile>("/api/me", result.accessToken);
      setToken(result.accessToken); setEmail(profile.email); setPassword("");
      if (profile.mustChangePassword) {
        setMustChangePassword(true); setZevs([]); setZevId("");
        return;
      }
      const accessible = await request<Page<Zev>>("/api/me/zevs?pageSize=100", result.accessToken);
      setMustChangePassword(false); setZevs(accessible.items); setZevId(accessible.items[0]?.id ?? "");
    } catch (problem) { setError(String(problem)); }
    finally { setBusy(false); }
  }

  useEffect(() => {
    let mounted = true;
    renewAccessToken().then(async (accessToken) => {
      if (!accessToken || !mounted) return;
      const profile = await request<MobileProfile>("/api/me", accessToken);
      if (mounted) { setEmail(profile.email); setMustChangePassword(profile.mustChangePassword); }
      if (profile.mustChangePassword) return;
      const accessible = await request<Page<Zev>>("/api/me/zevs?pageSize=100", accessToken);
      if (mounted) { setZevs(accessible.items); setZevId(accessible.items[0]?.id ?? ""); }
    }).catch((problem) => { if (mounted) setError(String(problem)); });
    return () => { mounted = false; };
  }, []);

  async function logout() {
    const refreshToken = await SecureStore.getItemAsync("zev-refresh");
    if (refreshToken) {
      try { await request("/api/auth/mobile/logout", "", { refreshToken }); }
      catch { setError("Odjava na serveru nije potvrđena."); }
    }
    await SecureStore.deleteItemAsync("zev-refresh");
    setToken(""); setZevs([]); setZevId(""); setCards([]);
    setMustChangePassword(false); setProfileOpen(false);
    setCurrentPassword(""); setNewPassword(""); setConfirmPassword("");
  }

  async function changePassword() {
    if (newPassword !== confirmPassword) {
      setError("Lozinke se ne podudaraju."); return;
    }
    setBusy(true); setError("");
    try {
      const result = await request<MobileTokens>("/api/me/password/mobile", token,
        { currentPassword: mustChangePassword ? null : currentPassword,
          newPassword, confirmPassword });
      await SecureStore.setItemAsync("zev-refresh", result.refreshToken);
      setToken(result.accessToken); setCurrentPassword(""); setNewPassword(""); setConfirmPassword("");
      setMustChangePassword(false); setProfileOpen(false);
      const accessible = await request<Page<Zev>>("/api/me/zevs?pageSize=100", result.accessToken);
      setZevs(accessible.items); setZevId(accessible.items[0]?.id ?? "");
      Alert.alert("Lozinka je promijenjena.");
    } catch (problem) { setError(String(problem)); }
    finally { setBusy(false); }
  }

  useEffect(() => {
    if (!token || !zevId) return;
    let mounted = true;
    setBusy(true);
    Promise.all([
      request<Card[]>(`/api/me/finance/${zevId}`, token),
      request<Page<Ticket>>(`/api/zevs/${zevId}/maintenance/tickets`, token),
      request<Page<Announcement>>(`/api/zevs/${zevId}/community/announcements`, token),
      request<Page<Document>>(`/api/zevs/${zevId}/community/documents`, token),
      request<Page<Ballot>>(`/api/zevs/${zevId}/ballots`, token),
      request<Unit[]>("/api/me/property-units", token),
    ]).then(([finance, ticketPage, announcementPage, documentPage, ballotPage, propertyUnits]) => {
      if (!mounted) return;
      setCards(finance); setTickets(ticketPage.items); setAnnouncements(announcementPage.items);
      setDocuments(documentPage.items); setBallots(ballotPage.items);
      const availableUnits = propertyUnits.filter((unit) => unit.zevId === zevId);
      setUnits(availableUnits); setSelectedUnitId(availableUnits[0]?.id ?? ""); setError("");
    }).catch((problem) => { if (mounted) setError(String(problem)); })
      .finally(() => { if (mounted) setBusy(false); });
    return () => { mounted = false; };
  }, [token, zevId]);

  async function choosePhoto() {
    const result = await ImagePicker.launchImageLibraryAsync({ mediaTypes: ["images"], base64: true,
      quality: 0.6 });
    if (!result.canceled) setPhoto(result.assets[0]);
  }

  async function reportTicket() {
    if (!units.length || !category.trim() || !description.trim() || !location.trim()) {
      setError("Odaberite nekretninu i popunite prijavu."); return;
    }
    setBusy(true); setError("");
    try {
      const fileIds: string[] = [];
      if (photo?.base64) {
        const uploaded = await request<{ id: string }>(`/api/zevs/${zevId}/files`, token,
          { fileName: photo.fileName ?? "kvar.jpg", contentType: photo.mimeType ?? "image/jpeg",
            contentBase64: photo.base64 });
        fileIds.push(uploaded.id);
      }
      await request(`/api/zevs/${zevId}/maintenance/tickets`, token,
        { propertyUnitId: selectedUnitId, category, description, location, priority: "Normal", fileIds });
      setCategory(""); setDescription(""); setLocation(""); setPhoto(null);
      const result = await request<Page<Ticket>>(`/api/zevs/${zevId}/maintenance/tickets`, token);
      setTickets(result.items); Alert.alert("Prijava je sačuvana.");
    } catch (problem) { setError(String(problem)); }
    finally { setBusy(false); }
  }

  async function vote(ballotId: string, choice: "Yes" | "No") {
    if (!units.length) { setError("Nema povezane nekretnine za glasanje."); return; }
    try {
      await request(`/api/zevs/${zevId}/ballots/${ballotId}/votes`, token,
        { propertyUnitId: selectedUnitId, choice });
      Alert.alert("Glas je evidentiran.");
    } catch (problem) { setError(String(problem)); }
  }

  async function shareFile(path: string, fileName: string) {
    if (!apiBase) return;
    let file: File | null = null;
    try {
      let response = await fetch(`${apiBase}${path}`, { headers: { Authorization: `Bearer ${token}` } });
      if (response.status === 401) {
        const nextToken = await renewAccessToken();
        if (nextToken) response = await fetch(`${apiBase}${path}`,
          { headers: { Authorization: `Bearer ${nextToken}` } });
      }
      if (!response.ok) throw new Error(`Datoteka nije dostupna (${response.status}).`);
      const mimeType = response.headers.get("Content-Type")?.split(";", 1)[0] ?? "application/pdf";
      const extension = mimeType === "image/png" ? ".png" : mimeType === "image/jpeg" ? ".jpg" : ".pdf";
      file = new File(Paths.cache, `${Date.now()}-${fileName}${extension}`);
      file.write(new Uint8Array(await response.arrayBuffer()));
      if (!await Sharing.isAvailableAsync()) throw new Error("Dijeljenje datoteke nije dostupno na uređaju.");
      await Sharing.shareAsync(file.uri, { mimeType });
    } catch (problem) { setError(String(problem)); }
    finally { if (file?.exists) file.delete(); }
  }

  if (!token) return <SafeAreaView style={styles.root}><View style={styles.section}>
    <Text style={styles.title}>ZEV Platforma</Text><Text>Prijava etažnog vlasnika</Text>
    <TextInput style={styles.input} value={email} onChangeText={setEmail} placeholder="Email" autoCapitalize="none" />
    <TextInput style={styles.input} value={password} onChangeText={setPassword} placeholder="Lozinka" secureTextEntry />
    <Button title="Prijavi se" onPress={() => void login()} disabled={busy} />
    {error ? <Text style={styles.error}>{error}</Text> : null}
  </View></SafeAreaView>;

  if (mustChangePassword || profileOpen) return <SafeAreaView style={styles.root}>
    <ScrollView contentContainerStyle={styles.section}>
      <Text style={styles.title}>{mustChangePassword ? "Obavezna promjena lozinke" : "Moj profil"}</Text>
      <Text>{email}</Text>
      {!mustChangePassword && <TextInput style={styles.input} value={currentPassword}
        onChangeText={setCurrentPassword} placeholder="Trenutna lozinka" secureTextEntry />}
      <TextInput style={styles.input} value={newPassword} onChangeText={setNewPassword}
        placeholder="Nova lozinka" secureTextEntry />
      <TextInput style={styles.input} value={confirmPassword} onChangeText={setConfirmPassword}
        placeholder="Potvrdi novu lozinku" secureTextEntry />
      <Text>Najmanje 12 znakova, veliko i malo slovo, broj i specijalni znak.</Text>
      <Button title="Promijeni lozinku" onPress={() => void changePassword()} disabled={busy} />
      <Button title={mustChangePassword ? "Odjavi se" : "Otkaži"}
        onPress={mustChangePassword ? () => void logout() : () => setProfileOpen(false)} />
      {busy && <ActivityIndicator />}{error ? <Text style={styles.error}>{error}</Text> : null}
    </ScrollView>
  </SafeAreaView>;

  return <SafeAreaView style={styles.root}><ScrollView contentContainerStyle={styles.section}>
    <Text style={styles.title}>Moj ZEV</Text>
    {zevs.map((zev) => <Button key={zev.id} title={`${zevId === zev.id ? "✓ " : ""}${zev.name}`}
      onPress={() => setZevId(zev.id)} />)}
    <Button title="Odjavi se" onPress={() => void logout()} />
    <Button title="Moj profil · Promijeni lozinku" onPress={() => setProfileOpen(true)} />
    {busy && <ActivityIndicator />}{error ? <Text style={styles.error}>{error}</Text> : null}
    <Text style={styles.heading}>Saldo i zaduženja</Text>
    {cards.map((card) => <View key={card.personId}>
      <Text>Saldo: {card.balance.toFixed(2)}</Text>
      {card.charges.map((charge) => <View key={charge.id}><Text>{charge.period} · {charge.amount.toFixed(2)} ·
        otvoreno {charge.open.toFixed(2)} · {charge.reference}</Text>
        <Button title="PDF uplatnica" onPress={() => void shareFile(
          `/api/zevs/${zevId}/charges/${charge.id}/payment-slip.pdf`, `uplatnica-${charge.id}`)} /></View>)}
      <Text style={styles.heading}>Uplate</Text>
      {card.payments.map((payment) => <Text key={payment.id}>{payment.paidOn} · {payment.amount.toFixed(2)}</Text>)}
    </View>)}
    <Text style={styles.heading}>Prijava kvara</Text>
    <Text>Nekretnina</Text>
    {units.map((unit) => <Button key={unit.id} title={`${selectedUnitId === unit.id ? "✓ " : ""}${unit.code}`}
      onPress={() => setSelectedUnitId(unit.id)} />)}
    <TextInput style={styles.input} value={category} onChangeText={setCategory} placeholder="Kategorija" />
    <TextInput style={styles.input} value={description} onChangeText={setDescription} placeholder="Opis" multiline />
    <TextInput style={styles.input} value={location} onChangeText={setLocation} placeholder="Lokacija" />
    <Button title={photo ? "Fotografija odabrana" : "Dodaj fotografiju"} onPress={() => void choosePhoto()} />
    <Button title="Pošalji prijavu" onPress={() => void reportTicket()} disabled={busy || !selectedUnitId} />
    <Text style={styles.heading}>Moje prijave</Text>
    {tickets.map((ticket) => <Text key={ticket.id}>{ticket.category} · {ticket.status}: {ticket.description}</Text>)}
    <Text style={styles.heading}>Obavještenja</Text>
    {announcements.map((item) => <Text key={item.id}>{item.title}: {item.content}</Text>)}
    <Text style={styles.heading}>Dokumenti</Text>
    {documents.map((item) => <View key={item.id}><Text>{item.title} · {item.type}</Text>
      <Button title="Otvori dokument" onPress={() => void shareFile(
        `/api/zevs/${zevId}/community/documents/${item.id}/download`, `dokument-${item.id}`)} /></View>)}
    <Text style={styles.heading}>Glasanja</Text>
    {ballots.map((item) => <View key={item.id}><Text>{item.title}: {item.question} ({item.status})</Text>
      {item.status === "Open" && <View style={styles.buttons}><Button title="Za" onPress={() => void vote(item.id, "Yes")} />
        <Button title="Protiv" onPress={() => void vote(item.id, "No")} /></View>}</View>)}
  </ScrollView></SafeAreaView>;
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: "#f5f7fa" },
  section: { padding: 24, gap: 12 },
  title: { fontSize: 28, fontWeight: "700", color: "#172b4d" },
  heading: { fontSize: 20, fontWeight: "600", marginTop: 18, color: "#172b4d" },
  input: { backgroundColor: "white", borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 8, padding: 12 },
  error: { color: "#a61b1b" },
  buttons: { flexDirection: "row", gap: 16 },
});
