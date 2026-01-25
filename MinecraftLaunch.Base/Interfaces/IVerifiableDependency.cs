namespace MinecraftLaunch.Base.Interfaces;

public interface IVerifiableDependency
{
    long? Size { get; }
    string Sha1 { get; }
}