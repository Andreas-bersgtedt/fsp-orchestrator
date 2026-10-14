import createClient from "openapi-fetch";

import type { paths as CatalogPaths } from "./catalog.js";
import type { paths as ConfigBuilderPaths } from "./config-builder.js";
import type { paths as LitePaths } from "./lite.js";
import type { paths as ManagerPaths } from "./manager.js";


export const createCatalogClient = (baseUrl: string, headers: HeadersInit = {}) =>
  createClient<CatalogPaths>({ baseUrl, headers });

export const createConfigBuilderClient = (baseUrl: string, headers: HeadersInit = {}) =>
  createClient<ConfigBuilderPaths>({ baseUrl, headers });

export const createLiteClient = (baseUrl: string, headers: HeadersInit = {}) =>
  createClient<LitePaths>({ baseUrl, headers });

export const createManagerClient = (baseUrl: string, headers: HeadersInit = {}) =>
  createClient<ManagerPaths>({ baseUrl, headers });
