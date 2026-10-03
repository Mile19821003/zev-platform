import { createApiClient } from "./apiClient.ts";

export { ApiError, errorText } from "./apiClient.ts";
export type Page<T> = { page: number; pageSize: number; totalCount: number; items: T[] };
export type Profile = {
  id: string;
  email: string;
  isSystemAdministrator: boolean;
  mustChangePassword: boolean;
  memberships: { organizationId: string; zevId: string | null; role: string }[];
};
export type Organization = {
  id: string; code: string; name: string; addressLine: string | null; cityMunicipality: string | null;
  taxIdentifier: string | null; contactName: string | null; email: string | null; phone: string | null;
  activatedOn: string | null; deactivatedOn: string | null; isActive: boolean; rowVersion: string;
};
export type Zev = {
  id: string;
  organizationId: string;
  organizationName: string;
  code: string;
  name: string;
  sources?: string[];
  isOwner?: boolean;
  historicalSelfAccess?: boolean;
  canManageFinances?: boolean;
  canManageLegalCases?: boolean;
  canManageMaintenance?: boolean;
  isStaff?: boolean;
  permissions?: string[];
  addressLine: string | null;
  cityMunicipality: string | null;
  taxIdentifier: string | null;
  primaryBankAccount: string | null;
  reserveFundBankAccount: string | null;
  contactEmail: string | null;
  contactPhone: string | null;
  foundedOn: string | null;
  isActive: boolean;
  rowVersion: string;
};
export type Building = {
  id: string;
  organizationId: string;
  zevId: string;
  zevName: string;
  code: string;
  name: string;
  addressLine: string | null;
  isActive: boolean;
  rowVersion: string;
};
export type Entrance = {
  id: string;
  organizationId: string;
  zevId: string;
  buildingId: string;
  buildingName: string;
  code: string;
  name: string;
  isActive: boolean;
  rowVersion: string;
};
export type PropertyUnit = {
  id: string; organizationId: string; zevId: string; buildingId: string; entranceId: string;
  entranceName: string; code: string; type: string; floor: number | null;
  areaSquareMeters: number; coefficient: number; registeredMembers: number;
  isActive: boolean; rowVersion: string;
};
export type Person = {
  id: string; organizationId: string; userId: string | null; kind: string; displayName: string;
  userLinkStatus?: string;
  taxIdentifier: string | null; addressLine: string | null; cityMunicipality: string | null;
  email: string | null; phone: string | null; isActive: boolean; rowVersion: string;
};
export type Ownership = {
  id: string; propertyUnitId: string; personId: string; personName: string;
  validFrom: string; validTo: string | null; share: number; rowVersion: string;
};
export type User = { id: string; email: string; isActive: boolean; isSystemAdministrator: boolean;
  mustChangePassword: boolean; rowVersion: string };
export type Membership = {
  id: string;
  userId: string;
  userEmail: string;
  organizationId: string;
  organizationName: string;
  zevId: string | null;
  zevName: string | null;
  role: string;
  isActive: boolean;
  rowVersion: string;
};

export const callApi = createApiClient(import.meta.env.VITE_API_URL ?? "http://localhost:8080");
