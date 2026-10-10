// Device storage adapters: refresh token in the OS keychain/keystore, offline snapshot in the app sandbox.
import { File, Paths } from "expo-file-system";
import * as SecureStore from "expo-secure-store";
import type { TokenStore } from "./api";
import type { SnapshotStorage } from "./cache";

const refreshKey = "zev-refresh";

export const secureTokenStore: TokenStore = {
  get: () => SecureStore.getItemAsync(refreshKey),
  set: (refreshToken) => SecureStore.setItemAsync(refreshKey, refreshToken),
  remove: () => SecureStore.deleteItemAsync(refreshKey),
};

export const snapshotFileStorage: SnapshotStorage = {
  read: async () => {
    const file = new File(Paths.document, "zev-offline.json");
    return file.exists ? await file.text() : null;
  },
  write: async (content) => {
    const file = new File(Paths.document, "zev-offline.json");
    if (!file.exists) file.create();
    file.write(content);
  },
  remove: async () => {
    const file = new File(Paths.document, "zev-offline.json");
    if (file.exists) file.delete();
  },
};
