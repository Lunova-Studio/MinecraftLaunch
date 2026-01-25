using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Interfaces;

public interface IInstallEntry
{
    /// <summary>
    /// Minecraft 的版本号，例如 "1.20.1"
    /// </summary>
    string McVersion { get; }

    /// <summary>
    /// 显示给用户看的版本号，可能包含模组加载器的版本信息，例如 "47.1.0"
    /// </summary>
    string DisplayVersion { get; }

    /// <summary>
    /// 安装项的描述信息，用于 UI 展示或辅助说明
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 所属的模组加载器类型，例如 Forge、Fabric、Quilt 等
    /// </summary>
    ModLoaderType ModLoaderType { get; }
}