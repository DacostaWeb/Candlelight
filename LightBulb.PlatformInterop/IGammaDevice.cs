using System;

namespace LightBulb.PlatformInterop;

public interface IGammaDevice : IDisposable
{
    string? FailureReason => null;
    string? ApplyDiagnostics => null;
    bool SetGamma(double red, double green, double blue);
    void ResetGamma();
}
