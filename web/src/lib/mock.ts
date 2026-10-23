import type {
  AccountView,
  AccountsPayload,
  AppSettings,
  Bootstrap,
  CatalogPayload,
  ContentInstallResult,
  ContentJob,
  ContentProgress,
  DailyBlock,
  GameLogLine,
  GameStatus,
  InstalledContent,
  Instance,
  InstanceCard,
  JavaRuntime,
  JavaScanPayload,
  LaunchState,
  LoaderInspection,
  ModrinthHit,
  ModrinthVersionInfo,
  NewsItem,
  NoteItem,
  RemoteVersion,
  VersionInspection
} from './types';

type Emit = (event: string, data: any) => void;

interface MockProject {
  projectId: string;
  slug: string;
  title: string;
  description: string;
  author: string;
  type: 'mod' | 'resourcepack' | 'shader';
  categories: string[];
  downloads: number;
  follows: number;
  color: number;
  fileName: string;
  fileSize: number;
  versionNumber: string;
}

const sleep = (ms: number) => new Promise<void>((resolve) => window.setTimeout(resolve, ms));

const asset = (path: string) => new URL(path, document.baseURI).toString();

const nowIso = () => new Date().toISOString();

const daysAgo = (days: number) => new Date(Date.now() - days * 86400000).toISOString();

const strip = (path: string) => path.replace(/\\/g, '\\\\');

const ROOT = 'C:\\Users\\zxz33\\AppData\\Roaming\\Chisel';

const EMPTY_SETTINGS: AppSettings = {
  schema: 1,
  onboarded: true,
  lastInstanceId: '1.21.4-fabric',
  activeAccountId: 'acc-shanzi',
  downloadSource: 'auto',
  downloadConcurrency: 16,
  autoMemory: true,
  memoryMb: 4096,
  javaPath: null,
  autoDownloadJava: true,
  closeOnLaunch: false,
  showConsoleOnLaunch: true,
  customResolution: false,
  width: 1280,
  height: 720,
  jvmArgs: '',
  microsoftClientId: '',
  curseForgeApiKey: '',
  windowWidth: 1360,
  windowHeight: 860,
  windowMaximized: false
};

const PROJECTS: MockProject[] = [
  {
    projectId: 'sodium',
    slug: 'sodium',
    title: 'Sodium',
    description: '现代化渲染引擎，大幅提高帧率，同时保持画面干净。',
    author: 'CaffeineMC',
    type: 'mod',
    categories: ['优化', '渲染'],
    downloads: 48201933,
    follows: 19204,
    color: 0x4f8f8b,
    fileName: 'sodium-fabric-0.6.9+mc1.21.4.jar',
    fileSize: 1784322,
    versionNumber: '0.6.9+mc1.21.4'
  },
  {
    projectId: 'iris',
    slug: 'iris',
    title: 'Iris Shaders',
    description: '兼容 OptiFine 光影包的新一代光影加载器，与 Sodium 配合使用。',
    author: 'IrisShaders',
    type: 'mod',
    categories: ['光影', '优化'],
    downloads: 31055122,
    follows: 12091,
    color: 0x5b6b9e,
    fileName: 'iris-fabric-1.8.8+mc1.21.4.jar',
    fileSize: 9764310,
    versionNumber: '1.8.8+mc1.21.4'
  },
  {
    projectId: 'lithium',
    slug: 'lithium',
    title: 'Lithium',
    description: '不改变原版行为的前提下，优化游戏逻辑与 AI 的性能。',
    author: 'CaffeineMC',
    type: 'mod',
    categories: ['优化'],
    downloads: 22441892,
    follows: 8044,
    color: 0x8a9b62,
    fileName: 'lithium-fabric-0.15.3+mc1.21.4.jar',
    fileSize: 684231,
    versionNumber: '0.15.3+mc1.21.4'
  },
  {
    projectId: 'fabric-api',
    slug: 'fabric-api',
    title: 'Fabric API',
    description: '大多数 Fabric 模组依赖的基础接口集合。',
    author: 'FabricMC',
    type: 'mod',
    categories: ['前置', 'API'],
    downloads: 68120443,
    follows: 17220,
    color: 0xb1986a,
    fileName: 'fabric-api-0.115.6+1.21.4.jar',
    fileSize: 2183410,
    versionNumber: '0.115.6+1.21.4'
  },
  {
    projectId: 'mod-menu',
    slug: 'modmenu',
    title: 'Mod Menu',
    description: '在游戏内查看和管理已安装模组，显示更新与依赖信息。',
    author: 'Prospector',
    type: 'mod',
    categories: ['管理', '界面'],
    downloads: 41231008,
    follows: 9931,
    color: 0x7a8d76,
    fileName: 'modmenu-13.0.3.jar',
    fileSize: 654312,
    versionNumber: '13.0.3'
  },
  {
    projectId: 'xaeros-minimap',
    slug: 'xaeros-minimap',
    title: "Xaero's Minimap",
    description: '清晰、轻量的小地图，支持路径点与服务器同步。',
    author: 'xaero96',
    type: 'mod',
    categories: ['地图', '界面'],
    downloads: 38910221,
    follows: 7832,
    color: 0x6f7e5b,
    fileName: 'xaeros_minimap-fabric-25.1.0.jar',
    fileSize: 1834231,
    versionNumber: '25.1.0'
  },
  {
    projectId: 'jei',
    slug: 'jei',
    title: 'Just Enough Items',
    description: '在物品栏旁查看合成配方与用途，模组包里的常客。',
    author: 'mezz',
    type: 'mod',
    categories: ['工具', '配方'],
    downloads: 52098112,
    follows: 11203,
    color: 0x9a8b5f,
    fileName: 'jei-1.21.4-fabric-19.21.0.247.jar',
    fileSize: 1284930,
    versionNumber: '19.21.0.247'
  },
  {
    projectId: 'appleskin',
    slug: 'appleskin',
    title: 'AppleSkin',
    description: '显示食物恢复数值与饱和度，让每一餐都更明白。',
    author: 'squeek502',
    type: 'mod',
    categories: ['界面', '信息'],
    downloads: 28431054,
    follows: 6114,
    color: 0xa56a51,
    fileName: 'appleskin-fabric-mc1.21-3.0.6.jar',
    fileSize: 104321,
    versionNumber: '3.0.6'
  },
  {
    projectId: 'create',
    slug: 'create',
    title: 'Create',
    description: '以旋转动力为核心的机械建造模组，把工程变成一种美学。',
    author: 'simibubi',
    type: 'mod',
    categories: ['科技', '建造'],
    downloads: 34128322,
    follows: 15443,
    color: 0xb08958,
    fileName: 'create-fabric-6.0.4+mc1.21.4.jar',
    fileSize: 21039842,
    versionNumber: '6.0.4+mc1.21.4'
  },
  {
    projectId: 'fresh-animations',
    slug: 'fresh-animations',
    title: 'Fresh Animations',
    description: '为原版生物注入细腻动画，保留原有气质。',
    author: 'FreshLX',
    type: 'resourcepack',
    categories: ['原版风', '动画'],
    downloads: 24331233,
    follows: 5103,
    color: 0x7a8b64,
    fileName: 'FreshAnimations_v1.10.2.zip',
    fileSize: 3420311,
    versionNumber: 'v1.10.2'
  },
  {
    projectId: 'stay-true',
    slug: 'stay-true',
    title: 'Stay True',
    description: '克制的原版风格材质包，保留材质之间的呼吸感。',
    author: 'tacomagic',
    type: 'resourcepack',
    categories: ['原版风', '16x'],
    downloads: 12093121,
    follows: 3204,
    color: 0x8a866b,
    fileName: 'StayTrue1.21.4.zip',
    fileSize: 1842103,
    versionNumber: '1.21.4'
  },
  {
    projectId: 'complementary',
    slug: 'complementary-reimagined',
    title: 'Complementary Reimagined',
    description: '兼顾观感与性能的光影包，适合日常游玩。',
    author: 'Complementary',
    type: 'shader',
    categories: ['光影', '中配'],
    downloads: 21039112,
    follows: 8893,
    color: 0x6d8a72,
    fileName: 'ComplementaryReimagined_r5.5.1.zip',
    fileSize: 5843322,
    versionNumber: 'r5.5.1'
  },
  {
    projectId: 'bsl',
    slug: 'bsl-shaders',
    title: 'BSL Shaders',
    description: '柔和的光线与色彩，适合风景与慢节奏的存档。',
    author: 'capttatsu',
    type: 'shader',
    categories: ['光影', '中配'],
    downloads: 18320113,
    follows: 6104,
    color: 0x5b7584,
    fileName: 'BSL_v10.0.zip',
    fileSize: 4021901,
    versionNumber: 'v10.0'
  }
];

const NEWS: NewsItem[] = [
  {
    title: '26.3 发布：把天空留给风',
    tag: 'news',
    category: '更新',
    date: '2026-10-15',
    text: '新的云层系统与洞穴音场一同到来，远处的地平线终于有了层次。',
    imageUrl: '/news/cobblemon.webp',
    link: 'https://www.minecraft.net/'
  },
  {
    title: 'Minecraft Live 2026：三件正在发生的事',
    tag: 'news',
    category: '活动',
    date: '2026-10-11',
    text: '开发团队分享了更安静的洞穴、更慢的夜晚，以及一座会记住你来过的村庄。',
    imageUrl: null,
    link: 'https://www.minecraft.net/'
  },
  {
    title: '一封来自世界的信',
    tag: 'news',
    category: '社区',
    date: '2026-10-04',
    text: '建筑与红石作者们用三个月建起一条横跨海洋的铁路，存档已经公开。',
    imageUrl: null,
    link: 'https://www.minecraft.net/'
  }
];

const PATCHES: NewsItem[] = [
  {
    title: '26.3 · 天空与风',
    tag: 'patch',
    category: '正式版 · 26.3',
    date: '2026-10-15',
    text: '新增分层云层与音乐盒变体，修复 37 个社区反馈的问题。'
  },
  {
    title: '26.2 · 洞穴里的一场雨',
    tag: 'patch',
    category: '正式版 · 26.2',
    date: '2026-09-22',
    text: '重新调整洞穴环境音，加入苔藓与钟乳石的自然生长规则。'
  },
  {
    title: '26.1 · 缓慢的春天',
    tag: 'patch',
    category: '正式版 · 26.1',
    date: '2026-08-18',
    text: '村民日程重新平衡，新增两种可培育的树，减少夜晚的怪物密度。'
  }
];

const DAILY: DailyBlock = {
  id: 'copper_block',
  nameZh: '铜块',
  nameEn: 'Block of Copper',
  note: '刚放下去时很亮，过一阵子会慢慢变旧，正好。',
  dataUrl: null,
  gameVersion: null,
  accent: '#b06f51'
};

const CATALOG: Array<[string, RemoteVersion['type'], string, number]> = [
  ['26.3', 'release', '2026-10-15', 25],
  ['26.2', 'release', '2026-09-22', 25],
  ['26.1', 'release', '2026-08-18', 25],
  ['1.21.10', 'release', '2026-06-02', 21],
  ['1.21.8', 'release', '2026-03-11', 21],
  ['1.21.5', 'release', '2025-11-20', 21],
  ['1.21.4', 'release', '2025-09-17', 21],
  ['1.21.1', 'release', '2025-05-14', 21],
  ['1.20.6', 'release', '2025-02-08', 21],
  ['1.20.4', 'release', '2024-11-19', 17],
  ['1.20.1', 'release', '2024-08-02', 17],
  ['1.19.4', 'release', '2024-03-14', 17],
  ['1.18.2', 'release', '2023-12-06', 17],
  ['1.17.1', 'release', '2023-08-23', 16],
  ['1.16.5', 'release', '2023-04-12', 8],
  ['1.12.2', 'release', '2022-09-18', 8],
  ['1.8.9', 'release', '2022-03-04', 8],
  ['26.4-snapshot-3', 'snapshot', '2026-10-20', 25],
  ['26.4-snapshot-2', 'snapshot', '2026-10-08', 25],
  ['26.4-snapshot-1', 'snapshot', '2026-09-27', 25],
  ['1.21.11-pre2', 'snapshot', '2026-05-19', 21],
  ['1.21.9-rc1', 'snapshot', '2026-03-02', 21],
  ['1.20.5-pre1', 'snapshot', '2025-01-12', 21]
];

const LOADER_FABRIC: LoaderInspection = {
  type: 'fabric',
  label: 'Fabric',
  available: true,
  note: null,
  versions: [
    { version: '0.19.5', stable: true, build: 341, gameVersion: null },
    { version: '0.19.4', stable: true, build: 339, gameVersion: null },
    { version: '0.18.4', stable: true, build: 320, gameVersion: null },
    { version: '0.16.14', stable: true, build: 280, gameVersion: null }
  ]
};

const LOADER_QUILT: LoaderInspection = {
  type: 'quilt',
  label: 'Quilt',
  available: true,
  note: '该版本只有测试版加载器',
  versions: [
    { version: '0.20.0-beta.9', stable: false, build: 120, gameVersion: null },
    { version: '0.20.0-beta.8', stable: false, build: 118, gameVersion: null }
  ]
};

function cardOf(instance: Instance, overrides: Partial<InstanceCard> = {}): InstanceCard {
  return {
    instance,
    gameDir: `${ROOT}\\instances\\${instance.id}\\game`,
    ready: true,
    javaReady: true,
    modCount: 0,
    saveCount: 0,
    sizeBytes: 0,
    playtimeLabel: instance.playSeconds >= 3600
      ? `${Math.floor(instance.playSeconds / 3600)} 小时 ${Math.floor((instance.playSeconds % 3600) / 60)} 分钟`
      : instance.playSeconds >= 60
        ? `${Math.floor(instance.playSeconds / 60)} 分钟`
        : '不到一分钟',
    loaderLabel: instance.loader === 'fabric' ? 'Fabric' : instance.loader === 'quilt' ? 'Quilt' : '原版',
    running: false,
    ...overrides
  };
}

function baseInstance(partial: Partial<Instance> & { id: string; displayName: string; gameVersion: string }): Instance {
  return {
    loader: 'vanilla',
    loaderVersion: null,
    profileId: null,
    createdAt: daysAgo(30),
    lastPlayedAt: null,
    playSeconds: 0,
    lastExitReason: null,
    lastExitCode: null,
    requiredJava: 21,
    javaPathOverride: null,
    memoryMbOverride: null,
    ...partial
  };
}

const initialInstances: InstanceCard[] = [
  cardOf(
    baseInstance({
      id: '1.21.4-fabric',
      displayName: '苔痕',
      gameVersion: '1.21.4',
      loader: 'fabric',
      loaderVersion: '0.19.5',
      profileId: 'fabric-loader-0.19.5-1.21.4',
      createdAt: daysAgo(46),
      lastPlayedAt: new Date(Date.now() - 3 * 3600000).toISOString(),
      playSeconds: 15840,
      requiredJava: 21
    }),
    { modCount: 12, saveCount: 3, sizeBytes: 1582934211 }
  ),
  cardOf(
    baseInstance({
      id: '1.20.1-vanilla',
      displayName: '旧港',
      gameVersion: '1.20.1',
      createdAt: daysAgo(120),
      lastPlayedAt: daysAgo(9),
      playSeconds: 41220,
      requiredJava: 17
    }),
    { modCount: 0, saveCount: 4, sizeBytes: 982134112 }
  ),
  cardOf(
    baseInstance({
      id: '26.3-vanilla',
      displayName: '纸灯',
      gameVersion: '26.3',
      createdAt: daysAgo(7),
      lastPlayedAt: daysAgo(2),
      playSeconds: 1860,
      requiredJava: 25
    }),
    { modCount: 0, saveCount: 1, sizeBytes: 1049283312 }
  )
];

const installedContent: Record<string, InstalledContent[]> = {
  '1.21.4-fabric': [
    {
      projectId: 'sodium',
      versionId: 'sodium-0.6.9',
      projectType: 'mod',
      title: 'Sodium',
      fileName: 'sodium-fabric-0.6.9+mc1.21.4.jar',
      iconUrl: null,
      versionNumber: '0.6.9+mc1.21.4',
      enabled: true,
      fileExists: true,
      installedAt: daysAgo(21),
      sizeBytes: 1784322
    },
    {
      projectId: 'fabric-api',
      versionId: 'fabric-api-0.115.6',
      projectType: 'mod',
      title: 'Fabric API',
      fileName: 'fabric-api-0.115.6+1.21.4.jar',
      iconUrl: null,
      versionNumber: '0.115.6+1.21.4',
      enabled: true,
      fileExists: true,
      installedAt: daysAgo(21),
      sizeBytes: 2183410
    },
    {
      projectId: 'iris',
      versionId: 'iris-1.8.8',
      projectType: 'mod',
      title: 'Iris Shaders',
      fileName: 'iris-fabric-1.8.8+mc1.21.4.jar',
      iconUrl: null,
      versionNumber: '1.8.8+mc1.21.4',
      enabled: true,
      fileExists: true,
      installedAt: daysAgo(14),
      sizeBytes: 9764310
    },
    {
      projectId: 'mod-menu',
      versionId: 'modmenu-13.0.3',
      projectType: 'mod',
      title: 'Mod Menu',
      fileName: 'modmenu-13.0.3.jar',
      iconUrl: null,
      versionNumber: '13.0.3',
      enabled: false,
      fileExists: true,
      installedAt: daysAgo(10),
      sizeBytes: 654312
    }
  ]
};

export function createMock(emit: Emit) {
  const query = new URLSearchParams(location.search);
  let settings: AppSettings = {
    ...EMPTY_SETTINGS,
    onboarded: !query.has('onboarding'),
    lastInstanceId: query.has('empty') ? null : EMPTY_SETTINGS.lastInstanceId
  };
  const accounts: AccountView[] = [
    {
      id: 'acc-shanzi',
      type: 'offline',
      name: 'shanzi',
      uuid: '44d7f90c-3ea1-3d47-8d28-13d90b9a6a11',
      isActive: true,
      hasSkin: true,
      expiresAt: null
    },
    {
      id: 'acc-ms',
      type: 'microsoft',
      name: 'ChiselCraft',
      uuid: '8d2e810f9cba4a2bb21d1e11c4d194ce',
      isActive: false,
      hasSkin: false,
      expiresAt: new Date(Date.now() + 864000000).toISOString()
    }
  ];
  let instances = query.has('empty') ? [] : initialInstances.map((card) => ({ ...card }));
  let runningId: string | null = null;
  let launchSeq = 0;
  let logs: GameLogLine[] = [];
  let status: GameStatus | null = null;
  let javaRuntimes: JavaRuntime[] = [
    {
      path: `${ROOT}\\java\\21\\bin\\java.exe`,
      javawPath: `${ROOT}\\java\\21\\bin\\javaw.exe`,
      major: 21,
      version: '21.0.7',
      vendor: 'Eclipse Temurin',
      managed: true,
      is64BitHint: true
    },
    {
      path: 'C:\\Program Files\\Eclipse Adoptium\\jdk-25.0.1.8-hotspot\\bin\\java.exe',
      javawPath: 'C:\\Program Files\\Eclipse Adoptium\\jdk-25.0.1.8-hotspot\\bin\\javaw.exe',
      major: 25,
      version: '25.0.1',
      vendor: 'Eclipse Temurin',
      managed: false,
      is64BitHint: true
    },
    {
      path: 'C:\\Program Files\\Java\\jre1.8.0_431\\bin\\java.exe',
      javawPath: 'C:\\Program Files\\Java\\jre1.8.0_431\\bin\\javaw.exe',
      major: 8,
      version: '1.8.0_431',
      vendor: 'Oracle / OpenJDK',
      managed: false,
      is64BitHint: false
    }
  ];
  const jobs = new Map<string, { cancelled: boolean }>();
  const contentJobs = new Map<string, ContentJob>();
  let javaSeq = 0;
  let notes: NoteItem[] = [
    {
      id: 'note-seed',
      text: '第一次进来，像推开一间没有人住的木屋。',
      reply: '木屋一直亮着。你来了，它才像有人住。',
      rare: false,
      createdAt: daysAgo(2)
    }
  ];

  const echoFor = (text: string): string => {
    if (text.includes('晚安')) return '晚安。灯留在门口，风替你看着。';
    if (text.includes('谢谢')) return '山收到了。它不太会说谢，就让石头稍微暖一点。';
    if (text.includes('你好')) return '你好。这里风大，进来坐。';
    return '收到了。山谷里多了一点声音。';
  };

  const accountsPayload = (): AccountsPayload => ({
    items: accounts.map((account) => ({ ...account })),
    activeId: accounts.find((account) => account.isActive)?.id ?? null,
    microsoftReady: Boolean(settings.microsoftClientId)
  });

  const cards = (): InstanceCard[] =>
    instances.map((card) => ({
      ...card,
      instance: { ...card.instance },
      running: card.instance.id === runningId
    }));

  const findCard = (id: string) => cards().find((card) => card.instance.id === id) ?? null;

  const launchState = (): LaunchState => ({
    running: runningId !== null,
    instanceId: runningId,
    status
  });

  const bootstrap = (): Bootstrap => ({
    app: {
      name: '凿境',
      codename: 'CHISEL',
      version: '0.1.1',
      dataRoot: ROOT,
      logsDir: `${ROOT}\\logs`
    },
    settings,
    accounts: accountsPayload(),
    instances: cards(),
    running: launchState(),
    memory: { totalMb: 32704, availableMb: 18312, recommendedMb: 6144, defaultMb: 6144 },
    window: { maximized: false },
    system: { os: 'windows', arch: 'x64', version: 'Microsoft Windows 10.0.26200' }
  });

  const catalogPayload = (): CatalogPayload => ({
    latest: { release: '26.3', snapshot: '26.4-snapshot-3' },
    versions: CATALOG.map(([id, type, date, java]) => ({
      id,
      type,
      url: `https://piston-meta.mojang.com/v1/packages/mock/${id}.json`,
      time: date,
      releaseTime: `${date}T10:00:00.000Z`,
      sha1: null
    })),
    installedIds: instances.map((card) => card.instance.gameVersion)
  });

  const inspect = (gameVersion: string): VersionInspection => {
    const entry = CATALOG.find(([id]) => id === gameVersion);
    const java = entry?.[3] ?? 21;
    const client = 41483720;
    const libraries = 38293110;
    const assets = 484894038;
    return {
      gameVersion,
      type: entry?.[1] ?? 'release',
      releaseTime: `${entry?.[2] ?? '2026-01-01'}T10:00:00.000Z`,
      javaMajor: java,
      assetBytes: assets,
      clientBytes: client,
      libraryBytes: libraries,
      totalBytes: assets + client + libraries,
      loaders: java <= 8
        ? [{ ...LOADER_FABRIC, available: false, note: '该版本不支持 Fabric', versions: [] }, { ...LOADER_QUILT, available: false, note: '该版本不支持 Quilt', versions: [] }]
        : [
            { ...LOADER_FABRIC, versions: LOADER_FABRIC.versions.map((v) => ({ ...v, gameVersion })) },
            { ...LOADER_QUILT, versions: LOADER_QUILT.versions.map((v) => ({ ...v, gameVersion })) }
          ]
    };
  };

  const installedFor = (instanceId: string): InstalledContent[] =>
    (installedContent[instanceId] ?? []).map((item) => ({ ...item }));

  const decodeProject = (projectId: string) => PROJECTS.find((project) => project.projectId === projectId) ?? null;

  const versionsFor = (project: MockProject, gameVersion: string): ModrinthVersionInfo[] => {
    const loader = project.type === 'mod' ? 'fabric' : '';
    return [0, 1, 2].map((index) => ({
      id: `${project.projectId}-v${index}`,
      projectId: project.projectId,
      name: index === 0 ? project.versionNumber : `${project.title} ${project.versionNumber}-${index}`,
      versionNumber: index === 0 ? project.versionNumber : `${project.versionNumber}.${index}`,
      versionType: index === 0 ? 'release' : index === 1 ? 'beta' : 'alpha',
      datePublished: daysAgo(index * 18 + 3),
      gameVersions: [gameVersion],
      loaders: loader ? [loader] : ['minecraft'],
      changelog: [
        index === 0
          ? '修复了上一版中若干兼容性问题，并针对当前游戏版本做了适配。'
          : '测试版本，可能包含尚未稳定的改动。'
      ],
      dependencies:
        project.projectId === 'sodium' || project.projectId === 'iris'
          ? [{ versionId: null, projectId: 'fabric-api', fileName: null, dependencyType: 'required' }]
          : [],
      file: {
        fileName: project.fileName,
        url: `https://cdn.modrinth.com/data/${project.projectId}/versions/mock/${project.fileName}`,
        size: project.fileSize,
        sha1: '',
        primary: true
      }
    }));
  };

  const hitOf = (project: MockProject, instanceId: string): ModrinthHit => {
    const installed = installedFor(instanceId).some((item) => item.projectId === project.projectId);
    return {
      projectId: project.projectId,
      slug: project.slug,
      title: project.title,
      description: project.description,
      author: project.author,
      iconUrl: '',
      downloads: project.downloads,
      follows: project.follows,
      categories: project.categories,
      projectType: project.type,
      versions: ['1.21.4', '1.21.1', '1.20.1'],
      latestVersion: project.versionNumber,
      color: project.color,
      installed,
      installedCompatible: installed
    };
  };

  const emitLog = (instanceId: string, text: string, stderr = false) => {
    const line: GameLogLine = {
      instanceId,
      sequence: logs.length + 1,
      text,
      stderr,
      at: nowIso()
    };
    logs.push(line);
    if (logs.length > 1800) logs = logs.slice(-1800);
    emit('launch.logs', { instanceId, lines: [line] });
  };

  const updateCardCounts = (instanceId: string) => {
    const card = instances.find((item) => item.instance.id === instanceId);
    if (!card) return;
    const items = installedContent[instanceId] ?? [];
    card.modCount = items.filter((item) => item.projectType === 'mod').length;
    card.instance = { ...card.instance };
  };

  const emitContentProgress = (
    requestId: string,
    project: MockProject,
    received: number,
    total: number,
    stage = 'download'
  ) => {
    const job = contentJobs.get(requestId);
    if (!job) return;
    const progress: ContentProgress = {
      stage,
      message: stage === 'resolve' ? '正在确认兼容版本' : `正在下载 ${project.fileName}`,
      projectTitle: project.title,
      receivedBytes: received,
      totalBytes: total,
      done: false
    };
    job.progress = progress;
    emit('content.progress', { requestId, progress });
  };

  const runContent = async (
    requestId: string,
    project: MockProject,
    instanceId: string,
    gameVersion: string,
    update: boolean
  ) => {
    const job = contentJobs.get(requestId);
    if (!job) return;
    try {
      emitContentProgress(requestId, project, 0, project.fileSize, 'resolve');
      await sleep(420);
      emitContentProgress(requestId, project, 0, project.fileSize, 'download');
      for (const fraction of [0.08, 0.24, 0.47, 0.7, 0.92]) {
        await sleep(210);
        if (job.state !== 'running') return;
        emitContentProgress(requestId, project, Math.round(project.fileSize * fraction), project.fileSize);
      }
      const list = installedContent[instanceId] ?? [];
      const existing = list.find((item) => item.projectId === project.projectId);
      const item: InstalledContent = {
        projectId: project.projectId,
        versionId: project.projectId + '-v0',
        projectType: project.type,
        title: project.title,
        fileName: project.fileName,
        iconUrl: null,
        versionNumber: project.versionNumber,
        enabled: true,
        fileExists: true,
        installedAt: nowIso(),
        sizeBytes: project.fileSize
      };
      if (existing) Object.assign(existing, item);
      else list.push(item);
      installedContent[instanceId] = list;
      updateCardCounts(instanceId);
      const result: ContentInstallResult = { installed: [{ ...item }], warnings: [], updated: update };
      job.state = 'done';
      job.result = result;
      emit('content.done', { requestId, result, instanceId });
      emit('content.changed', { instanceId });
      emit('instances.changed', null);
    } catch {
      job.state = 'failed';
    } finally {
      contentJobs.delete(requestId);
    }
  };

  const runInstall = async (jobId: string, params: any) => {
    const job = jobs.get(jobId);
    if (!job) return;
    const loader = params.loader ?? 'vanilla';
    const gameVersion = params.gameVersion;
    const loaderInfo = loader === 'vanilla' ? '原版' : loader === 'fabric' ? 'Fabric' : 'Quilt';
    const totalFiles = 312;
    for (let index = 1; index <= 11; index++) {
      await sleep(260);
      if (job.cancelled) {
        jobs.delete(jobId);
        emit('install.cancelled', { jobId });
        return;
      }
      const stage = index < 3 ? 'libraries' : index < 9 ? 'assets' : 'finish';
      const label = index < 3 ? '下载运行库' : index < 9 ? '下载资源文件' : '整理实例';
      emit('install.progress', {
        jobId,
        stage,
        stageLabel: label,
        completedFiles: Math.round((totalFiles * index) / 11),
        totalFiles,
        completedBytes: Math.round(556000000 * (index / 11)),
        totalBytes: 556000000,
        currentItem: index < 3 ? `fabric-loader-${params.loaderVersion ?? '0.19.5'}` : `assets/objects/${index}f3a`,
        bytesPerSecond: 8250000,
        instanceId: null,
        done: false,
        failed: false
      });
    }
    const id = `${gameVersion}-${loader}`.replace(/-vanilla$/, '');
    const existing = instances.find((card) => card.instance.id === id);
    if (!existing) {
      const card = cardOf(
        baseInstance({
          id,
          displayName: params.displayName?.trim() || (loader === 'vanilla' ? gameVersion : `${gameVersion} · ${loaderInfo}`),
          gameVersion,
          loader,
          loaderVersion: loader === 'vanilla' ? null : params.loaderVersion ?? '0.19.5',
          profileId: loader === 'vanilla' ? null : `fabric-loader-${params.loaderVersion ?? '0.19.5'}-${gameVersion}`,
          createdAt: nowIso(),
          lastPlayedAt: null,
          requiredJava: inspect(gameVersion).javaMajor
        }),
        { modCount: 0, saveCount: 0, sizeBytes: 556000000 }
      );
      instances = [card, ...instances];
    }
    jobs.delete(jobId);
    emit('install.progress', {
      jobId,
      stage: 'done',
      stageLabel: '准备完成',
      completedFiles: totalFiles,
      totalFiles,
      completedBytes: 556000000,
      totalBytes: 556000000,
      currentItem: null,
      bytesPerSecond: 0,
      instanceId: id,
      done: true,
      failed: false
    });
    emit('install.done', { jobId, instanceId: id, instance: findCard(id) });
    emit('instances.changed', null);
  };

  const launchSequence = async (seq: number, instanceId: string) => {
    const steps: Array<[string, string, number, number]> = [
      ['checking', '正在核对版本与文件', 0.04, 300],
      ['java', '正在准备 Java 21', 0.14, 420],
      ['account', '正在准备账号', 0.34, 380],
      ['starting', '正在打开游戏窗口', 0.88, 650]
    ];
    for (const [state, message, progress, delay] of steps) {
      await sleep(delay);
      if (seq !== launchSeq) return;
      status = {
        instanceId,
        state,
        message,
        progress,
        bytesReceived: 0,
        bytesTotal: 0,
        exitCode: null,
        diagnosis: null,
        at: nowIso()
      };
      emit('launch.status', status);
      if (state === 'java') {
        emitLog(instanceId, '[main/INFO]: Loading Minecraft 1.21.4 with Fabric Loader 0.19.5');
        emitLog(instanceId, '[main/INFO]: Loading 12 mods:');
        emitLog(instanceId, '  - sodium 0.6.9+mc1.21.4');
        emitLog(instanceId, '  - iris 1.8.8+mc1.21.4');
        emitLog(instanceId, '  - fabric-api 0.115.6+1.21.4');
      }
      if (state === 'account') {
        emitLog(instanceId, '[Render thread/INFO]: Setting user: shanzi');
      }
      if (state === 'starting') {
        emitLog(instanceId, '[Render thread/INFO]: Backend library: LWJGL version 3.3.3+5');
        emitLog(instanceId, '[Render thread/INFO]: OpenGL debug message: Using hardware acceleration');
      }
    }
    await sleep(900);
    if (seq !== launchSeq) return;
    status = {
      instanceId,
      state: 'running',
      message: '游戏已启动',
      progress: 1,
      bytesReceived: 0,
      bytesTotal: 0,
      exitCode: null,
      diagnosis: null,
      at: nowIso()
    };
    emit('launch.status', status);
    emitLog(instanceId, '[Render thread/INFO]: Narrator library for x64 successfully loaded');
    emitLog(instanceId, '[Render thread/WARN]: Xaero\'s Minimap: Could not find waypoint file, creating a new one');
    emitLog(instanceId, '[Render thread/INFO]: Sound engine started');
    emitLog(instanceId, '[Render thread/INFO]: Created: 1024x512x4 minecraft:textures/atlas/blocks.png-atlas');
    emit('instances.changed', null);
  };

  const beginMicrosoft = async () => {
    emit('auth.waiting', { message: '正在向 Microsoft 请求登录代码' });
    await sleep(400);
    emit('auth.waiting', { message: '等待你在浏览器中完成登录' });
    await sleep(7000);
    if (!accounts.some((account) => account.id === 'acc-ms')) {
      accounts.push({
        id: 'acc-ms',
        type: 'microsoft',
        name: 'ChiselCraft',
        uuid: '8d2e810f9cba4a2bb21d1e11c4d194ce',
        isActive: false,
        hasSkin: false,
        expiresAt: new Date(Date.now() + 864000000).toISOString()
      });
    }
    const account = accounts.find((item) => item.id === 'acc-ms')!;
    accounts.forEach((item) => (item.isActive = item.id === account.id));
    emit('auth.complete', { account, accounts: accountsPayload() });
    emit('accounts.changed', accountsPayload());
  };

  const handlers: Record<string, (params: any) => any> = {
    'app.bootstrap': () => bootstrap(),
    'app.ping': () => ({ pong: true, at: nowIso() }),
    'catalog.list': () => catalogPayload(),
    'version.inspect': (params) => inspect(params.gameVersion),
    'instance.list': () => cards(),
    'instance.get': (params) => {
      const card = findCard(params.id);
      if (!card) throw new Error('实例不存在');
      return card;
    },
    'instance.install': (params) => {
      const jobId = `job-${Date.now()}`;
      jobs.set(jobId, { cancelled: false });
      void runInstall(jobId, params);
      return { jobId };
    },
    'install.cancel': () => {
      const job = [...jobs.entries()].find(([, value]) => !value.cancelled);
      if (!job) return { cancelled: false, jobId: null };
      job[1].cancelled = true;
      return { cancelled: true, jobId: job[0] };
    },
    'instance.update': (params) => {
      const card = instances.find((item) => item.instance.id === params.id);
      if (!card) throw new Error('实例不存在');
      if (params.displayName) card.instance.displayName = params.displayName;
      if (params.memoryMb !== undefined) {
        card.instance.memoryMbOverride = params.memoryMb > 0 ? params.memoryMb : null;
      }
      if (params.clearJava) card.instance.javaPathOverride = null;
      else if (params.javaPath) card.instance.javaPathOverride = params.javaPath;
      emit('instances.changed', null);
      return findCard(params.id);
    },
    'instance.rename': (params) => {
      const card = instances.find((item) => item.instance.id === params.id);
      if (!card) throw new Error('实例不存在');
      card.instance.displayName = params.name;
      emit('instances.changed', null);
      return findCard(params.id);
    },
    'instance.delete': (params) => {
      if (runningId === params.id) throw new Error('游戏正在运行，先结束游戏再删除实例。');
      instances = instances.filter((item) => item.instance.id !== params.id);
      emit('instances.changed', null);
      return { deleted: true };
    },
    'instance.openFolder': () => ({ opened: true }),
    'instance.sizes': () => ({
      sizes: Object.fromEntries(instances.map((card) => [card.instance.id, card.sizeBytes])),
      cached: false
    }),
    'launch.start': (params) => {
      if (runningId) throw new Error('已经有一个游戏在运行了。');
      const card = findCard(params.instanceId);
      if (!card) throw new Error('实例不存在');
      runningId = params.instanceId;
      const seq = ++launchSeq;
      logs = [];
      status = null;
      emit('instances.changed', null);
      void launchSequence(seq, params.instanceId);
      emit('launch.started', {
        instanceId: params.instanceId,
        pid: 18244,
        warnings: [],
        javaPath: javaRuntimes[0].path,
        memoryMb: 6144,
        startedAt: nowIso()
      });
      return { accepted: true, instanceId: params.instanceId };
    },
    'launch.stop': () => {
      if (!runningId) return { stopped: false };
      const instanceId = runningId;
      launchSeq++;
      runningId = null;
      status = {
        instanceId,
        state: 'exited',
        message: '你结束了这次游戏',
        progress: 1,
        bytesReceived: 0,
        bytesTotal: 0,
        exitCode: 0,
        diagnosis: null,
        at: nowIso()
      };
      emit('launch.status', status);
      emit('instances.changed', null);
      return { stopped: true };
    },
    'launch.state': () => launchState(),
    'logs.history': (params) => ({
      source: 'memory',
      instanceId: params.instanceId ?? runningId,
      lines: logs
    }),
    'logs.openFolder': () => ({ opened: true }),
    'settings.update': (patch) => {
      settings = { ...settings, ...patch };
      emit('settings.changed', settings);
      return settings;
    },
    'settings.snapshot': () => settings,
    'java.scan': () => ({
      runtimes: javaRuntimes,
      recommendedMajor: 21,
      selectedPath: javaRuntimes[0].path,
      preferredPath: settings.javaPath
    }) as JavaScanPayload,
    'java.describe': (params) => {
      const runtime = javaRuntimes.find((item) => item.path === params.path);
      if (!runtime) throw new Error('没有从这个路径识别到 Java');
      return runtime;
    },
    'java.addPath': (params) => {
      settings.javaPath = params.path;
      emit('settings.changed', settings);
      return javaRuntimes[0];
    },
    'java.download': (params) => {
      const major = params.major ?? 21;
      const seq = ++javaSeq;
      void (async () => {
        for (const fraction of [0.05, 0.2, 0.45, 0.7, 0.95]) {
          await sleep(320);
          if (seq !== javaSeq) return;
          emit('java.progress', {
            stage: 'download',
            major,
            receivedBytes: Math.round(58500000 * fraction),
            totalBytes: 58500000,
            message: `Temurin ${major} JRE`,
            done: false,
            path: null
          });
        }
        if (seq !== javaSeq) return;
        emit('java.progress', {
          stage: 'done',
          major,
          receivedBytes: 58500000,
          totalBytes: 58500000,
          message: null,
          done: true,
          path: `${ROOT}\\java\\${major}\\bin\\java.exe`
        });
        emit('java.done', javaRuntimes[0]);
      })();
      return { accepted: true, major };
    },
    'java.cancel': () => {
      javaSeq++;
      emit('java.cancelled', {});
      return { cancelled: true };
    },
    'accounts.list': () => accountsPayload(),
    'accounts.addOffline': (params) => {
      const name = String(params.name ?? '').trim();
      if (!/^[A-Za-z0-9_]{3,16}$/.test(name)) throw new Error('离线用户名需要 3–16 位，只能包含字母、数字和下划线');
      const account: AccountView = {
        id: `acc-${Date.now()}`,
        type: 'offline',
        name,
        uuid: '00000000-0000-3000-8000-000000000000',
        isActive: false,
        hasSkin: false,
        expiresAt: null
      };
      accounts.push(account);
      emit('accounts.changed', accountsPayload());
      return account;
    },
    'accounts.select': (params) => {
      accounts.forEach((account) => (account.isActive = account.id === params.id));
      settings.activeAccountId = params.id;
      emit('accounts.changed', accountsPayload());
      return accountsPayload();
    },
    'accounts.remove': (params) => {
      const index = accounts.findIndex((account) => account.id === params.id);
      if (index >= 0) accounts.splice(index, 1);
      if (!accounts.some((account) => account.isActive) && accounts[0]) accounts[0].isActive = true;
      emit('accounts.changed', accountsPayload());
      return accountsPayload();
    },
    'accounts.setSkin': (params) => {
      const account = accounts.find((item) => item.id === params.accountId);
      if (!account) throw new Error('账号不存在');
      account.hasSkin = true;
      emit('accounts.changed', accountsPayload());
      return account;
    },
    'accounts.skin': (params) => {
      const account = accounts.find((item) => item.id === params.id);
      return { accountId: params.id, dataUrl: account?.hasSkin ? asset('skins/default.png') : null };
    },
    'accounts.microsoft.begin': () => {
      void beginMicrosoft();
      return {
        userCode: 'K7Q9-P2LM',
        verificationUri: 'https://www.microsoft.com/link',
        verificationUriComplete: 'https://www.microsoft.com/link?otc=K7Q9-P2LM',
        expiresIn: 900,
        copied: true,
        opened: true
      };
    },
    'accounts.microsoft.cancel': () => ({ cancelled: true }),
    'accounts.addAuthlib': (params) => {
      const account = {
        id: `acc-authlib-${Date.now()}`,
        type: 'authlib',
        name: params.username || 'Authlib',
        uuid: '00000000-0000-0000-0000-000000000000',
        authServerName: 'LittleSkin',
        isActive: true,
        hasSkin: false,
        expiresAt: null
      };
      accounts.forEach((item) => (item.isActive = false));
      accounts.push(account);
      emit('accounts.changed', accountsPayload());
      return account;
    },
    'shader.recommend': (params) => {
      const card = findCard(params.id);
      const loader = card?.instance.loader ?? 'vanilla';
      const fabricLike = loader === 'fabric' || loader === 'quilt';
      return {
        kind: fabricLike ? 'iris' : 'optifine',
        installed: loader === 'optifine',
        supported: fabricLike || loader === 'vanilla',
        label: fabricLike ? 'Iris' : 'OptiFine',
        hint: fabricLike ? '安装 Iris 与 Sodium，为光影提供支持' : '安装 OptiFine，原版即可使用光影'
      };
    },
    'shader.install': (params) => ({ kind: params.kind, job: { requestId: `mock-shader-${Date.now()}` } }),
    'modpack.pick': () => ({ path: null, preview: null }),
    'modpack.scan': () => ({
      candidates: [
        {
          id: 'p1', path: 'C:/Users/demo/AppData/Roaming/PrismLauncher/instances/TerraFirma/.minecraft',
          kind: 'prism', launcher: 'Prism Launcher', name: 'TerraFirma 机械工坊',
          gameVersion: '1.20.1', loader: 'fabric', loaderVersion: '0.15.11',
          modCount: 184, sizeBytes: 1_284_000_000, hasSaves: true
        },
        {
          id: 'p2', path: 'C:/Users/demo/AppData/Roaming/.minecraft/versions/1.20.1-Forge',
          kind: 'minecraft', launcher: '官方启动器 / PCL', name: '1.20.1-Forge',
          gameVersion: '1.20.1', loader: 'forge', loaderVersion: '47.2.0',
          modCount: 62, sizeBytes: 412_000_000, hasSaves: false
        },
        {
          id: 'p3', path: 'C:/Users/demo/Downloads/Cobblemon.mrpack',
          kind: 'archive', launcher: 'Modrinth 整合包', name: 'Cobblemon',
          gameVersion: '', loader: 'vanilla', loaderVersion: null,
          modCount: 0, sizeBytes: 86_000_000, hasSaves: false
        }
      ],
      roots: ['PrismLauncher/instances', '.minecraft/versions', 'Downloads']
    }),
    'curseforge.installLink': () => { throw new Error('浏览器预览不能导入 CurseForge 项目'); },
    'modrinth.search': (params) => {
      const instance = findCard(params.instanceId);
      if (!instance) throw new Error('实例不存在');
      const type = params.projectType ?? 'mod';
      if (type === 'mod' && instance.instance.loader === 'vanilla') {
        throw new Error('这个实例没有安装模组加载器，不能加载模组。');
      }
      const text = String(params.query ?? '').toLowerCase();
      let hits = PROJECTS.filter((project) => project.type === type)
        .filter((project) => !text || `${project.title} ${project.description} ${project.author}`.toLowerCase().includes(text))
        .map((project) => hitOf(project, params.instanceId));
      if (params.sort === 'downloads') hits.sort((a, b) => b.downloads - a.downloads);
      if (params.sort === 'newest') hits.sort((a, b) => a.title.localeCompare(b.title));
      return { hits, offset: 0, limit: 20, totalHits: hits.length };
    },
    'modrinth.versions': (params) => {
      const project = decodeProject(params.projectId);
      const instance = findCard(params.instanceId);
      if (!project || !instance) throw new Error('项目不存在');
      return versionsFor(project, instance.instance.gameVersion);
    },
    'modrinth.install': (params) => {
      const project = decodeProject(params.projectId);
      const instance = findCard(params.instanceId);
      if (!project || !instance) throw new Error('项目不存在');
      const requestId = `content-${Date.now()}`;
      const job: ContentJob = {
        requestId,
        projectId: project.projectId,
        title: project.title,
        instanceId: instance.instance.id,
        progress: null,
        state: 'running'
      };
      contentJobs.set(requestId, job);
      void runContent(requestId, project, instance.instance.id, instance.instance.gameVersion, false);
      return { accepted: true, requestId };
    },
    'modrinth.update': (params) => {
      const project = decodeProject(params.projectId);
      const instance = findCard(params.instanceId);
      if (!project || !instance) throw new Error('项目不存在');
      const requestId = `content-${Date.now()}`;
      contentJobs.set(requestId, {
        requestId,
        projectId: project.projectId,
        title: project.title,
        instanceId: instance.instance.id,
        progress: null,
        state: 'running'
      });
      void runContent(requestId, project, instance.instance.id, instance.instance.gameVersion, true);
      return { accepted: true, requestId };
    },
    'modrinth.list': (params) => installedFor(params.id),
    'modrinth.toggle': (params) => {
      const item = (installedContent[params.instanceId] ?? []).find((entry) => entry.projectId === params.projectId);
      if (!item) throw new Error('没有在实例清单里找到这个内容');
      item.enabled = params.enabled;
      emit('content.changed', { instanceId: params.instanceId });
      return item;
    },
    'modrinth.remove': (params) => {
      installedContent[params.instanceId] = (installedContent[params.instanceId] ?? []).filter(
        (item) => item.projectId !== params.projectId
      );
      updateCardCounts(params.instanceId);
      emit('content.changed', { instanceId: params.instanceId });
      return { removed: true };
    },
    'content.cancel': (params) => {
      const job = contentJobs.get(params.id);
      if (!job) return { cancelled: false };
      job.state = 'cancelled';
      contentJobs.delete(params.id);
      emit('content.cancelled', { requestId: params.id });
      return { cancelled: true };
    },
    'news.list': () => ({ news: NEWS, patches: PATCHES }),
    'block.daily': () => DAILY,
    'notes.list': () => ({ notes: notes.map((note) => ({ ...note })) }),
    'notes.add': (params) => {
      const text = String(params.text ?? '').replace(/\s+/g, ' ').trim();
      if (!text) throw new Error('还没有写下什么。');
      if (text.length > 80) throw new Error('留言最多 80 个字');
      const note: NoteItem = {
        id: `note-${Date.now()}`,
        text,
        reply: echoFor(text),
        rare: /苦力怕|creeper|凿境/.test(text),
        createdAt: nowIso()
      };
      notes = [note, ...notes].slice(0, 60);
      return note;
    },
    'notes.remove': (params) => {
      const before = notes.length;
      notes = notes.filter((note) => note.id !== params.id);
      return { removed: notes.length !== before };
    },
    'crash.last': () => null,
    'dialog.pickJava': () => ({ path: javaRuntimes[1].path }),
    'shell.openPath': () => ({ opened: true }),
    'shell.openUrl': (params) => {
      try {
        window.open(params.url, '_blank', 'noopener');
      } catch {
        /* ignore */
      }
      return { opened: true };
    },
    'shell.openData': () => ({ opened: true }),
    'window.minimize': () => ({ minimized: true }),
    'window.toggleMaximize': () => ({ maximized: false }),
    'window.close': () => ({ closing: true }),
    'window.drag': () => ({ dragging: true }),
    'clipboard.write': async (params) => {
      try {
        await navigator.clipboard.writeText(String(params.text ?? ''));
        return { copied: true };
      } catch {
        return { copied: false };
      }
    }
  };

  return {
    request(method: string, params?: any): Promise<any> {
      const handler = handlers[method];
      if (!handler) return Promise.reject(new Error(`未知的请求：${method}`));
      return Promise.resolve()
        .then(() => handler(params ?? {}))
        .catch((error: Error) => {
          throw error;
        });
    }
  };
}
