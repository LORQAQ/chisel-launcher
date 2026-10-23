export interface AppSettings {
  schema: number;
  onboarded: boolean;
  lastInstanceId: string | null;
  activeAccountId: string | null;
  downloadSource: 'auto' | 'official' | 'mirror';
  downloadConcurrency: number;
  autoMemory: boolean;
  memoryMb: number | null;
  javaPath: string | null;
  autoDownloadJava: boolean;
  closeOnLaunch: boolean;
  showConsoleOnLaunch: boolean;
  customResolution: boolean;
  width: number;
  height: number;
  jvmArgs: string;
  microsoftClientId: string;
  curseForgeApiKey: string;
  windowWidth: number;
  windowHeight: number;
  windowMaximized: boolean;
}

export interface Instance {
  id: string;
  displayName: string;
  gameVersion: string;
  loader: 'vanilla' | 'fabric' | 'quilt' | string;
  loaderVersion: string | null;
  profileId: string | null;
  createdAt: string;
  lastPlayedAt: string | null;
  playSeconds: number;
  lastExitReason: string | null;
  lastExitCode: number | null;
  requiredJava: number | null;
  javaPathOverride: string | null;
  memoryMbOverride: number | null;
}

export interface InstanceCard {
  instance: Instance;
  gameDir: string;
  ready: boolean;
  javaReady: boolean;
  modCount: number;
  saveCount: number;
  sizeBytes: number;
  playtimeLabel: string;
  loaderLabel: string;
  hasIcon?: boolean;
  iconVersion?: number;
  running: boolean;
}

export interface AccountView {
  id: string;
  type: 'offline' | 'microsoft' | 'authlib' | string;
  name: string;
  uuid: string;
  authServerName?: string | null;
  isActive: boolean;
  hasSkin: boolean;
  expiresAt: string | null;
}

export interface AccountsPayload {
  items: AccountView[];
  activeId: string | null;
  microsoftReady?: boolean;
  curseForgeReady?: boolean;
}

export interface MemoryInfo {
  totalMb: number;
  availableMb: number;
  recommendedMb: number;
  defaultMb: number;
}

export interface Bootstrap {
  app: {
    name: string;
    codename: string;
    version: string;
    dataRoot: string;
    logsDir: string;
  };
  settings: AppSettings;
  accounts: AccountsPayload;
  instances: InstanceCard[];
  running: LaunchState;
  memory: MemoryInfo;
  window: { maximized: boolean };
  system: { os: string; arch: string; version: string };
}

export interface RemoteVersion {
  id: string;
  type: 'release' | 'snapshot' | 'old_beta' | 'old_alpha' | string;
  url: string;
  time: string;
  releaseTime: string;
  sha1?: string | null;
}

export interface LatestVersions {
  release: string;
  snapshot: string;
}

export interface CatalogPayload {
  latest: LatestVersions;
  versions: RemoteVersion[];
  installedIds: string[];
}

export interface LoaderVersionOption {
  version: string;
  stable: boolean;
  build: number;
  gameVersion: string | null;
}

export interface LoaderInspection {
  type: string;
  label: string;
  available: boolean;
  note: string | null;
  versions: LoaderVersionOption[];
}

export interface VersionInspection {
  gameVersion: string;
  type: string;
  releaseTime: string;
  javaMajor: number;
  assetBytes: number;
  clientBytes: number;
  libraryBytes: number;
  totalBytes: number;
  loaders: LoaderInspection[];
}

export interface InstallRequest {
  gameVersion: string;
  loader: 'vanilla' | 'fabric' | 'quilt' | 'forge' | 'neoforge' | string;
  loaderVersion?: string | null;
  displayName?: string | null;
  modpackPath?: string | null;
  localInstancePath?: string | null;
}

export interface LocalPackCandidate {
  id: string;
  path: string;
  kind: string;
  launcher: string;
  name: string;
  gameVersion: string;
  loader: string;
  loaderVersion: string | null;
  modCount: number;
  sizeBytes: number;
  hasSaves: boolean;
}

export interface LocalPackScanResult {
  candidates: LocalPackCandidate[];
  roots: string[];
}

export interface InstallProgress {
  jobId: string;
  stage: string;
  stageLabel: string;
  completedFiles: number;
  totalFiles: number;
  completedBytes: number;
  totalBytes: number;
  currentItem: string | null;
  bytesPerSecond: number;
  instanceId: string | null;
  done: boolean;
  failed: boolean;
}

export interface GameLogLine {
  instanceId: string;
  sequence: number;
  text: string;
  stderr: boolean;
  at: string;
}

export interface CrashDiagnosis {
  severity: string;
  title: string;
  summary: string;
  evidence: string | null;
  suggestions: string[];
  suspectedMods: string[];
  latestLogPath: string | null;
  crashReportPath: string | null;
  exitCode: number;
}

export interface GameStatus {
  instanceId: string;
  state: string;
  message: string;
  progress: number;
  bytesReceived: number;
  bytesTotal: number;
  exitCode: number | null;
  diagnosis: CrashDiagnosis | null;
  at: string;
}

export interface LaunchState {
  running: boolean;
  instanceId: string | null;
  status: GameStatus | null;
}

export interface LaunchStartResult {
  instanceId: string;
  pid: number;
  warnings: string[];
  javaPath: string;
  memoryMb: number;
  startedAt: string;
}

export interface NewsItem {
  title: string;
  tag?: string | null;
  category?: string | null;
  date?: string | null;
  text?: string | null;
  imageUrl?: string | null;
  link?: string | null;
}

export interface DailyBlock {
  id: string;
  nameZh: string;
  nameEn: string;
  note: string;
  dataUrl: string | null;
  gameVersion: string | null;
  accent: string;
}

export interface NoteItem {
  id: string;
  text: string;
  reply: string;
  rare: boolean;
  createdAt: string;
}

export interface InstanceSizesPayload {
  sizes: Record<string, number>;
  cached: boolean;
}

export interface JavaRuntime {
  path: string;
  javawPath: string;
  major: number;
  version: string;
  vendor: string;
  managed: boolean;
  is64BitHint: boolean;
}

export interface JavaScanPayload {
  runtimes: JavaRuntime[];
  recommendedMajor: number;
  selectedPath: string | null;
  preferredPath: string | null;
}

export interface JavaProgress {
  stage: string;
  major: number;
  receivedBytes: number;
  totalBytes: number;
  message: string | null;
  done: boolean;
  path: string | null;
}

export interface ModrinthHit {
  projectId: string;
  slug: string;
  title: string;
  description: string;
  author: string;
  iconUrl: string;
  downloads: number;
  follows: number;
  categories: string[];
  projectType: string;
  versions: string[];
  latestVersion: string | null;
  color: number | null;
  installed: boolean;
  installedCompatible: boolean;
}

export interface ModrinthSearchResult {
  hits: ModrinthHit[];
  offset: number;
  limit: number;
  totalHits: number;
}

export interface ModrinthDependencyInfo {
  versionId: string | null;
  projectId: string | null;
  fileName: string | null;
  dependencyType: string;
}

export interface ModrinthFileInfo {
  fileName: string;
  url: string;
  size: number;
  sha1: string;
  primary: boolean;
}

export interface ModrinthVersionInfo {
  id: string;
  projectId: string;
  name: string;
  versionNumber: string;
  versionType: string;
  datePublished: string;
  gameVersions: string[];
  loaders: string[];
  changelog: string[];
  dependencies: ModrinthDependencyInfo[];
  file: ModrinthFileInfo | null;
}

export interface InstalledContent {
  projectId: string;
  versionId: string;
  projectType: string;
  title: string;
  fileName: string;
  iconUrl: string | null;
  versionNumber: string | null;
  enabled: boolean;
  fileExists: boolean;
  installedAt: string;
  sizeBytes: number;
}

export interface ContentProgress {
  stage: string;
  message: string;
  projectTitle: string | null;
  receivedBytes: number;
  totalBytes: number;
  done: boolean;
}

export interface ContentInstallResult {
  installed: InstalledContent[];
  warnings: string[];
  updated: boolean;
}

export interface ContentJob {
  requestId: string;
  projectId: string;
  title: string;
  instanceId: string;
  progress: ContentProgress | null;
  state: 'running' | 'done' | 'failed' | 'cancelled';
  message?: string;
  result?: ContentInstallResult;
}

export interface DeviceCodeInfo {
  userCode: string;
  verificationUri: string;
  verificationUriComplete?: string;
  expiresIn: number;
  copied?: boolean;
  opened?: boolean;
}

export interface AuthAccountPayload {
  account: AccountView;
  accounts: AccountsPayload;
}

export type PageId = 'home' | 'versions' | 'content' | 'settings';

export type ToastTone = 'info' | 'success' | 'error' | 'warn';

export interface ToastItem {
  id: number;
  text: string;
  tone: ToastTone;
  detail?: string;
}

export interface ModpackPreview {
  path: string;
  kind: 'mrpack' | 'curseforge' | string;
  name: string;
  version: string;
  gameVersion: string;
  loader: string;
  loaderVersion: string | null;
  fileCount: number;
  totalBytes: number;
  summary: string | null;
  warnings: string[];
}

export interface ShaderRecommendation {
  kind: 'iris' | 'optifine' | string;
  installed: boolean;
  supported: boolean;
  label: string;
  hint: string;
}

export interface ShaderProgress {
  jobId: string;
  stage: string;
  message: string;
  receivedBytes?: number;
  totalBytes?: number;
  bytesPerSecond?: number;
}
