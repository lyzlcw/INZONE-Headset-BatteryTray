namespace MixTray;

public interface IBatteryReader : IDisposable
{
    (int battery, bool charging) Read();
    string Method { get; }
}
