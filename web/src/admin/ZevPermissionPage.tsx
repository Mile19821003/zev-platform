import { useEffect, useState } from "react";
import { callApi, errorText } from "../api";
import { useChangeRevision } from "../useChangeRevision.ts";
import PermissionMatrix, { PermissionUser } from "./PermissionMatrix";

export default function ZevPermissionPage({ token, zevId, zevs }: { token: string; zevId: string;
  zevs: { id: string; name: string }[] }) {
  const [users, setUsers] = useState<PermissionUser[]>([]);
  const [selectedUser, setSelectedUser] = useState("");
  const [search, setSearch] = useState("");
  const [error, setError] = useState("");
  const revision = useChangeRevision("access");
  useEffect(() => {
    let current = true;
    callApi<PermissionUser[]>(`/api/zevs/${zevId}/access/users?search=${encodeURIComponent(search)}`, {}, token)
      .then((items) => { if (current) { setUsers(items); setError(""); } })
      .catch((problem) => { if (current) setError(errorText(problem)); });
    return () => { current = false; };
  }, [token, zevId, search, revision]);
  return <><h1>Uloge i pristupi</h1><p>Prava važe samo za izabrani ZEV. Nije dozvoljeno mijenjati vlastita prava.</p>
    {error && <p className="error" role="alert">{error}</p>}
    <PermissionMatrix token={token} zevId={zevId} zevs={zevs} users={users}
      selectedUser={selectedUser} onSelectUser={setSelectedUser} userSearch={search} onUserSearch={setSearch} />
  </>;
}
