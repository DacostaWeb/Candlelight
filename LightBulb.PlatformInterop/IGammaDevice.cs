using System;

namespace LightBulb.PlatformInterop;

public interface IGammaDevice : IDisposable
{
    string? FailureReason => null;
    string? ApplyDiagnostics => null;
    bool? DidWriteGamma => null;
    bool SetGamma(double red, double green, double blue);
    bool EnsureGamma(double red, double green, double blue) => SetGamma(red, green, blue);
    void ResetGamma();
}
