import { useEffect, useMemo, useState } from "react";
import { ActivityIndicator, Alert, Button, SafeAreaView, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import * as ImagePicker from "expo-image-picker";
import { File, Paths } from "expo-file-system";
import * as Sharing from "expo-sharing";
import { createApiClient, NetworkError, type MobileTokens } from "./src/api";
import { createOfflineCache } from "./src/cache";
import { loadOwnerCards, toFinanceView, type InvoiceView, type OwnerCard } from "./src/finance";
import { ballotStatusLabels, documentTypeLabels, formatTimestamp, label, maintenanceStatusLabels } from "./src/format";
import { secureTokenStore, snapshotFileStorage } from "./src/storage";

type Page<T> = { items: T[] };
type Zev = { id: string; code: string; name: string };
type Ticket = { id: string; category: string; description: string; status: string };
type Announcement = { id: string; title: string; content: string };
type Document = { id: string; title: string; type: string };
type Ballot = { id: string; title: string; question: string; status: string };
type Unit = { id: string; zevId: string; code: string };
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
  // Timestamp of the cached data shown while the API is unreachable; empty when online.
  const [offlineSince, setOfflineSince] = useState("");
  const [reloadKey, setReloadKey] = useState(0);
  const [zevs, setZevs] = useState<Zev[]>([]);
  const [zevId, setZevId] = useState("");
  const [cards, setCards] = useState<OwnerCard[]>([]);
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
  const client = useMemo(() => createApiClient({ baseUrl: apiBase, allowInsecure: __DEV__,
    tokens: secureTokenStore, onAccessToken: setToken }), []);
  const cache = useMemo(() => createOfflineCache(snapshotFileStorage), []);
  const request = client.request;
  const finance = useMemo(() => toFinanceView(zevId, cards), [zevId, cards]);
  const offline = Boolean(offlineSince);

  function showZevs(items: Zev[]) {
    setZevs(items);
    setZevId((current) => items.some((zev) => zev.id === current) ? current : items[0]?.id ?? "");
  }

  async function loadSession(accessToken: string) {
    const profile = await request<MobileProfile>("/api/me", accessToken);
    setEmail(profile.email); setMustChangePassword(profile.mustChangePassword);
    if (profile.mustChangePassword) { setZevs([]); setZevId(""); return; }
    const accessible = await request<Page<Zev>>("/api/me/zevs?pageSize=100", accessToken);
    showZevs(accessible.items); setOfflineSince("");
    await cache.saveProfile(profile.email, accessible.items.map(({ id, code, name }) => ({ id, code, name })));
  }

  // Without a connection the last saved profile and ZEV list are shown read-only, if this device has a session.
  async function showOfflineSnapshot(): Promise<boolean> {
    const snapshot = await cache.load();
    if (!snapshot?.email || !await secureTokenStore.get()) return false;
    setEmail(snapshot.email); setMustChangePassword(false); showZevs(snapshot.zevs);
    setOfflineSince(snapshot.savedAt);
    return true;
  }

  async function login() {
    setBusy(true); setError("");
    try {
      const result = await request<MobileTokens>("/api/auth/mobile/login", "", { email, password });
      await secureTokenStore.set(result.refreshToken);
      setPassword("");
      await loadSession(result.accessToken);
      setToken(result.accessToken);
    } catch (problem) { setError(message(problem)); }
    finally { setBusy(false); }
  }

  async function resume() {
    setBusy(true);
    try {
      const accessToken = await client.renewAccessToken();
      if (!accessToken) { setOfflineSince(""); setToken(""); return; }
      await loadSession(accessToken);
      setReloadKey((key) => key + 1); setError("");
    } catch (problem) {
      if (!(problem instanceof NetworkError && await showOfflineSnapshot())) setError(message(problem));
    } finally { setBusy(false); }
  }

  useEffect(() => { void resume(); }, []);

  async function logout() {
    const refreshToken = await secureTokenStore.get();
    if (refreshToken && !offline) {
      try { await request("/api/auth/mobile/logout", "", { refreshToken }); }
      catch { setError("Odjava na serveru nije potvrđena."); }
    }
    await secureTokenStore.remove();
    await cache.clear();
    setToken(""); setOfflineSince(""); setZevs([]); setZevId(""); setCards([]);
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
      await secureTokenStore.set(result.refreshToken);
      setToken(result.accessToken); setCurrentPassword(""); setNewPassword(""); setConfirmPassword("");
      setProfileOpen(false);
      await loadSession(result.accessToken);
      Alert.alert("Lozinka je promijenjena.");
    } catch (problem) { setError(message(problem)); }
    finally { setBusy(false); }
  }

  useEffect(() => {
    if (!zevId || (!token && !offline)) return;
    let mounted = true;
    async function showCachedFinance() {
      const cached = (await cache.load())?.finance[zevId];
      if (!mounted) return;
      setCards(cached?.cards ?? []);
      setTickets([]); setAnnouncements([]); setDocuments([]); setBallots([]); setUnits([]);
      if (cached) setOfflineSince(cached.savedAt);
    }
    if (offline) { void showCachedFinance(); return () => { mounted = false; }; }
    setBusy(true);
    Promise.all([
      loadOwnerCards((path) => request<unknown>(path, token), zevId),
      request<Page<Ticket>>(`/api/zevs/${zevId}/maintenance/tickets`, token),
      request<Page<Announcement>>(`/api/zevs/${zevId}/community/announcements`, token),
      request<Page<Document>>(`/api/zevs/${zevId}/community/documents`, token),
      request<Page<Ballot>>(`/api/zevs/${zevId}/ballots`, token),
      request<Unit[]>("/api/me/property-units", token),
    ]).then(async ([ownerCards, ticketPage, announcementPage, documentPage, ballotPage, propertyUnits]) => {
      if (!mounted) return;
      setCards(ownerCards); setTickets(ticketPage.items); setAnnouncements(announcementPage.items);
      setDocuments(documentPage.items); setBallots(ballotPage.items);
      const availableUnits = propertyUnits.filter((unit) => unit.zevId === zevId);
      setUnits(availableUnits); setSelectedUnitId(availableUnits[0]?.id ?? ""); setError("");
      await cache.saveFinance(zevId, ownerCards);
    }).catch(async (problem) => {
      if (!mounted) return;
      if (problem instanceof NetworkError && (await cache.load())?.finance[zevId]) {
        setOfflineSince(new Date().toISOString());
        return;
      }
      setError(message(problem));
    }).finally(() => { if (mounted) setBusy(false); });
    return () => { mounted = false; };
  }, [token, zevId, offline, reloadKey]);

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
    } catch (problem) { setError(message(problem)); }
    finally { setBusy(false); }
  }

  async function vote(ballotId: string, choice: "Yes" | "No") {
    if (!units.length) { setError("Nema povezane nekretnine za glasanje."); return; }
    try {
      await request(`/api/zevs/${zevId}/ballots/${ballotId}/votes`, token,
        { propertyUnitId: selectedUnitId, choice });
      Alert.alert("Glas je evidentiran.");
    } catch (problem) { setError(message(problem)); }
  }

  async function shareFile(path: string, fileName: string) {
    let file: File | null = null;
    setError("");
    try {
      const response = await client.download(path, token);
      const mimeType = response.headers.get("Content-Type")?.split(";", 1)[0] ?? "application/pdf";
      const extension = mimeType === "image/png" ? ".png" : mimeType === "image/jpeg" ? ".jpg" : ".pdf";
      file = new File(Paths.cache, `${Date.now()}-${fileName}${extension}`);
      file.write(new Uint8Array(await response.arrayBuffer()));
      if (!await Sharing.isAvailableAsync()) throw new Error("Dijeljenje datoteke nije dostupno na uređaju.");
      await Sharing.shareAsync(file.uri, { mimeType });
    } catch (problem) { setError(message(problem)); }
    finally { if (file?.exists) file.delete(); }
  }

  function renderInvoice(item: InvoiceView) {
    return <View key={item.id} style={styles.card}>
      <Text style={styles.strong}>{item.title}</Text>
      <Text>{item.detail}</Text>
      <Text style={item.open ? styles.warning : styles.muted}>{item.status}</Text>
      {!offline && item.slips.map((slip) => <Button key={slip.chargeId} title={slip.label}
        onPress={() => void shareFile(slip.path, `uplatnica-${item.number}`)} />)}
    </View>;
  }

  if (!token && !offline) return <SafeAreaView style={styles.root}><View style={styles.section}>
    <Text style={styles.title}>ZEV Platforma</Text><Text>Prijava etažnog vlasnika</Text>
    <TextInput style={styles.input} value={email} onChangeText={setEmail} placeholder="Email" autoCapitalize="none" />
    <TextInput style={styles.input} value={password} onChangeText={setPassword} placeholder="Lozinka" secureTextEntry />
    <Button title="Prijavi se" onPress={() => void login()} disabled={busy} />
    {busy && <ActivityIndicator />}{error ? <Text style={styles.error}>{error}</Text> : null}
  </View></SafeAreaView>;

  if (!offline && (mustChangePassword || profileOpen)) return <SafeAreaView style={styles.root}>
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
    <Text style={styles.muted}>{email}</Text>
    {offline && <View style={styles.offline}>
      <Text style={styles.strong}>Bez veze sa serverom</Text>
      <Text>Prikazani su sačuvani podaci od {formatTimestamp(offlineSince)} (samo za čitanje).</Text>
      <Button title="Pokušaj ponovo" onPress={() => void resume()} disabled={busy} />
    </View>}
    {zevs.map((zev) => <Button key={zev.id} title={`${zevId === zev.id ? "✓ " : ""}${zev.name}`}
      onPress={() => setZevId(zev.id)} />)}
    <Button title="Odjavi se" onPress={() => void logout()} />
    {!offline && <Button title="Moj profil · Promijeni lozinku" onPress={() => setProfileOpen(true)} />}
    {busy && <ActivityIndicator />}{error ? <Text style={styles.error}>{error}</Text> : null}

    <Text style={styles.heading}>Saldo</Text>
    {finance.balances.length === 0 && <Text style={styles.muted}>Nema finansijskih podataka.</Text>}
    {finance.balances.map((balance) => <View key={balance.key} style={styles.card}>
      <Text style={styles.strong}>{balance.title}</Text><Text>{balance.detail}</Text></View>)}
    <Text style={styles.heading}>Računi</Text>
    {finance.invoices.length === 0 && finance.legacyCharges.length === 0
      && <Text style={styles.muted}>Nema izdatih računa.</Text>}
    {finance.invoices.map(renderInvoice)}
    {finance.legacyCharges.map(renderInvoice)}
    <Text style={styles.heading}>Uplate</Text>
    {finance.payments.length === 0 && <Text style={styles.muted}>Nema evidentiranih uplata.</Text>}
    {finance.payments.map((payment) => <Text key={payment.key}>{payment.text}</Text>)}
    {finance.partial && <Text style={styles.muted}>Prikazane su najnovije stavke kartice.</Text>}

    {!offline && <>
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
      {tickets.map((ticket) => <Text key={ticket.id}>{ticket.category} · {label(maintenanceStatusLabels, ticket.status)}: {ticket.description}</Text>)}
      <Text style={styles.heading}>Obavještenja</Text>
      {announcements.map((item) => <Text key={item.id}>{item.title}: {item.content}</Text>)}
      <Text style={styles.heading}>Dokumenti</Text>
      {documents.map((item) => <View key={item.id}><Text>{item.title} · {label(documentTypeLabels, item.type)}</Text>
        <Button title="Otvori dokument" onPress={() => void shareFile(
          `/api/zevs/${zevId}/community/documents/${item.id}/download`, `dokument-${item.id}`)} /></View>)}
      <Text style={styles.heading}>Glasanja</Text>
      {ballots.map((item) => <View key={item.id}><Text>{item.title}: {item.question} ({label(ballotStatusLabels, item.status)})</Text>
        {item.status === "Open" && <View style={styles.buttons}><Button title="Za" onPress={() => void vote(item.id, "Yes")} />
          <Button title="Protiv" onPress={() => void vote(item.id, "No")} /></View>}</View>)}
    </>}
  </ScrollView></SafeAreaView>;
}

function message(problem: unknown): string {
  return problem instanceof Error ? problem.message : String(problem);
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: "#f5f7fa" },
  section: { padding: 24, gap: 12 },
  title: { fontSize: 28, fontWeight: "700", color: "#172b4d" },
  heading: { fontSize: 20, fontWeight: "600", marginTop: 18, color: "#172b4d" },
  input: { backgroundColor: "white", borderWidth: 1, borderColor: "#cbd5e1", borderRadius: 8, padding: 12 },
  card: { backgroundColor: "white", borderRadius: 8, padding: 12, gap: 4, borderWidth: 1, borderColor: "#e2e8f0" },
  offline: { backgroundColor: "#fff7e6", borderRadius: 8, padding: 12, gap: 6, borderWidth: 1, borderColor: "#f0c36d" },
  strong: { fontWeight: "600", color: "#172b4d" },
  muted: { color: "#5e6c84" },
  warning: { color: "#9a5b00" },
  error: { color: "#a61b1b" },
  buttons: { flexDirection: "row", gap: 16 },
});
