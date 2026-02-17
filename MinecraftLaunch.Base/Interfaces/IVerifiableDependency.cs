using MinecraftLaunch.Base.Models.SHA1;

namespace MinecraftLaunch.Base.Interfaces;

public interface IVerifiableDependency {
    long? Size { get; }
    Sha1Data? Sha1 { get; }
}
