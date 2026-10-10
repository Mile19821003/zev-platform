# Deployment i rollback

Trenutni status je Functional Beta, ne produkcijsko odobrenje. `compose.yaml` služi za lokalni razvoj, ne za javni deployment bez TLS reverse proxyja, secret store-a, najmanje privilegovanog SQL naloga i operativnog monitoringa.

## Kontrolisani postupak

1. Potvrditi otvorene poslovne odluke, UAT i security/dependency gateove iz `FINAL_TECHNICAL_CLOSURE.md`.
2. Na stagingu: backup, SQL schema/migration diff, backend testovi sa stvarno aktiviranim SQL varijablama, web build, mobilni typecheck i uređajski UAT.
3. Osigurati vanjske tajne, TLS, CORS/Web origin, SQL i storage kapacitet, backup/restore raspored i alarme.
4. U održavanom prozoru zaustaviti upis, uzeti i verifikovati backup. Primijeniti samo pregledane EF migracije; nikad ne resetovati postojeću bazu.
5. Objaviti API i web iste verzije. Provjeriti live/ready, login/refresh, svaku personu, negativni tenant IDOR, jedan nefinansijski i jedan kontrolisani finansijski smoke.
6. Prvih 24 sata pratiti SQL greške/deadlock, 401/403, import/sravnjenje, audit, API latenciju, storage i uspjeh backup-a.

## Rollback

Zaustaviti upise, sačuvati incident log i verziju migracije. Vratiti staru API/web verziju samo ako je kompatibilna s novom shemom. Inače uz odobren plan restore-a vratiti verifikovani backup u **novu** bazu, provjeriti konzistentnost i tek onda preusmjeriti saobraćaj. Ne koristiti `docker compose down -v`, ad hoc brisanje volumena ni restore preko postojeće baze.

Detalji lokalnog pokretanja: `LOCAL_DEVELOPMENT.md`; oporavak baze: `BACKUP_RESTORE.md`.

Produkcijski stack (`compose.prod.yaml`, TLS proxy, migracije, najmanje privilegovan SQL nalog) i operativna backup/restore
procedura sa skriptama: `OPERATIONAL_BACKUP_RESTORE.md`.
