using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// An IConfiguration with nothing in it, for constructors that only read a
/// config value to fall back to a default when it's absent (the usual
/// `config.GetValue&lt;T&gt;("Some:Key") ?? default` pattern this codebase uses
/// throughout). GetSection must return a real, empty section rather than throw
/// — GetValue calls it internally, so a naive stub that throws there would
/// break construction before the test even starts.
/// </summary>
public sealed class NullConfiguration : IConfiguration
{
    public static readonly NullConfiguration Instance = new();

    public string? this[string key] { get => null; set { } }

    public IConfigurationSection GetSection(string key) => new EmptySection(key);

    public IEnumerable<IConfigurationSection> GetChildren() => Enumerable.Empty<IConfigurationSection>();

    public IChangeToken GetReloadToken() => throw new NotSupportedException();

    private sealed class EmptySection : IConfigurationSection
    {
        public EmptySection(string key) { Key = key; Path = key; }
        public string? this[string key] { get => null; set { } }
        public string Key { get; }
        public string Path { get; }
        public string? Value { get => null; set { } }
        public IConfigurationSection GetSection(string key) => new EmptySection(key);
        public IEnumerable<IConfigurationSection> GetChildren() => Enumerable.Empty<IConfigurationSection>();
        public IChangeToken GetReloadToken() => throw new NotSupportedException();
    }
}
