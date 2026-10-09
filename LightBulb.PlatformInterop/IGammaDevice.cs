using System;

namespace LightBulb.PlatformInterop;

public interface IGammaDevice : IDisposable
{
    bool SetGamma(double red, double green, double blue);
    void ResetGamma();
}
