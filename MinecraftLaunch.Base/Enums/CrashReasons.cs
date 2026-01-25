namespace MinecraftLaunch.Base.Enums;

public enum CrashReasons
{
    // Minecraft
    FileOrContentCheckFailed,

    SpecificBlockCausedCrash,
    SpecificEntityCausedCrash,
    TextureTooLargeOrInsufficientGraphicsConfig,
    ShaderOrResourcePackCausedOpenGL1282Error,
    UnableToLoadTexture,

    // Mod
    ModConfigCausedGameCrash,

    ModMixinFailed,
    ModLoaderError,
    ModInitializationFailed,
    ModFileDecompressed,
    TooManyModsExceededIdLimit,
    ModCausedGameCrash,
    ModInstalledRepeatedly,

    // ModLoader
    OptiFineIncompatibleWithForge,

    FabricError,
    FabricErrorWithSolution,
    ForgeError,
    LowVersionForgeIncompatibleWithHighVersionJava,
    MultipleForgeInVersionJson,
    OptiFineCausedWorldLoadingFailure,

    // Log
    CrashLogStackAnalysisFoundKeyword,

    CrashLogStackAnalysisFoundModName,
    MCLogStackAnalysisFoundKeyword,

    // Jvm
    InsufficientMemory,

    UsingJDK,
    GraphicsCardDoesNotSupportOpenGL,
    UsingOpenJ9,
    JavaVersionTooHigh,
    UnsupportedJavaClassVersionError,
    Using32BitJavaCausedInsufficientJVMMemory,

    // Player
    PlayerTriggeredDebugCrash
}