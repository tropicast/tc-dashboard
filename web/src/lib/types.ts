import type { components } from '../api/schema';

export type Problem =
  components['schemas']['ProblemDetails'] | components['schemas']['HttpValidationProblemDetails'];
export type Me = components['schemas']['MeResponse'];
export type Station = components['schemas']['StationResponse'];
export type Tenant = components['schemas']['TenantResponse'];
export type Status = components['schemas']['StationStatusResponse'];
export type Credential = components['schemas']['CredentialResponse'];
