using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LightBulb.Core;
using LightBulb.ViewModels;

namespace LightBulb.Diagnostics;

// Exercises the actual compiled UI and bindings using synthetic monitors only.
internal static class PreviewVerification
{
    public static async Task RunAsync(Window window, MainViewModel model, string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            await Task.Delay(800);
            Require(
                Dispatcher.UIThread.CheckAccess(),
                "UI verification did not resume on the UI thread."
            );
            var controls = model.Dashboard.Displays;
            Require(controls.Displays.Count == 2, "Two preview displays were not discovered.");
            controls.SelectedDisplay = controls.Displays[0];
            controls.ApplyPreset(controls.Presets.First(p => p.Id == "day"));
            var internalId = controls.SelectedDisplay.Id;
            controls.SelectedDisplay = controls.Displays[1];
            await Task.Delay(100);
            var presetButton = window
                .GetVisualDescendants()
                .OfType<Button>()
                .First(b => b.CommandParameter is ColorPreset { Id: "oled-red" });
            Require(presetButton.Command is not null, "The preset button command did not bind.");
            presetButton.Command!.Execute(presetButton.CommandParameter);
            await Task.Delay(150);
            Require(
                controls.Temperature == 500 && controls.Brightness == 15 && controls.IsManual,
                "The actual preset button did not apply red-only settings."
            );
            Require(
                controls.Displays.First(d => d.Id == internalId).Current.Brightness == 1,
                $"Internal readout: {controls.Displays.First(d => d.Id == internalId).Current}. Enabled={model.Dashboard.IsEnabled}, paused={model.Dashboard.IsPaused}."
            );
            var numeric = window
                .GetVisualDescendants()
                .OfType<NumericUpDown>()
                .First(n => n.Name == "TemperatureValue");
            numeric.SetCurrentValue(NumericUpDown.ValueProperty, 4000m);
            Require(
                controls.Temperature == 4000,
                "Numeric temperature input did not update the profile."
            );
            var temperatureSlider = window
                .GetVisualDescendants()
                .OfType<Slider>()
                .First(s => s.Name == "TemperatureSlider");
            temperatureSlider.SetCurrentValue(Slider.ValueProperty, 500d);
            Require(controls.Temperature == 500, "Temperature slider did not update the profile.");
            var brightnessValue = window
                .GetVisualDescendants()
                .OfType<NumericUpDown>()
                .First(n => n.Name == "BrightnessValue");
            brightnessValue.SetCurrentValue(NumericUpDown.ValueProperty, 40m);
            Require(
                controls.Brightness == 40,
                "Numeric brightness input did not update the profile."
            );
            var brightnessSlider = window
                .GetVisualDescendants()
                .OfType<Slider>()
                .First(s => s.Name == "BrightnessSlider");
            brightnessSlider.SetCurrentValue(Slider.ValueProperty, 15d);
            Require(controls.Brightness == 15, "Brightness slider did not update the profile.");
            Capture(window, directory, "manual.png");

            controls.UseExampleScheduleCommand.Execute(null);
            await Task.Delay(100);
            Require(
                controls.Schedule.Count == 3 && controls.IsSchedule,
                "The schedule command did not populate rows."
            );
            var lastValid = controls.Schedule[0].Time;
            controls.Schedule[0].Time = "25:00";
            Require(
                controls.ScheduleError.Length > 0,
                "Invalid hours did not show a validation message."
            );
            controls.ApplyPreset(controls.Presets.First(p => p.Id == "oled-red"));
            Require(
                controls.IsManual && controls.Temperature == 500,
                "An invalid schedule blocked a manual preset."
            );
            controls.ModeIndex = 2;
            controls.Schedule[0].Time = lastValid;
            Require(controls.ScheduleError.Length == 0, "Corrected hours did not clear the error.");
            await Task.Delay(100);
            var remove = window
                .GetVisualDescendants()
                .OfType<Button>()
                .First(b => b.CommandParameter is ViewModels.Components.SchedulePointViewModel);
            Require(remove.Command is not null, "The schedule remove command did not bind.");
            Capture(window, directory, "schedule.png");
            remove.Command!.Execute(remove.CommandParameter);
            Require(
                controls.Schedule.Count == 2,
                "The bound schedule command did not remove a row."
            );
            controls.PresetName = "Verification preset";
            controls.SavePresetCommand.Execute(null);
            var custom = controls.Presets.Single(p => p.Name == "Verification preset");
            controls.DeletePresetCommand.Execute(custom);
            Require(controls.Presets.All(p => p.Id != custom.Id), "Custom preset deletion failed.");

            controls.SelectedDisplay = controls.Displays[0];
            Require(
                controls.IsManual && controls.Brightness == 100,
                "Switching displays lost the independent profile."
            );
            controls.ModeIndex = 0;
            await Task.Delay(100);
            Capture(window, directory, "day-night.png");
            controls.SaveNow();
            var protection = window
                .GetVisualDescendants()
                .OfType<ToggleSwitch>()
                .Single(toggle => toggle.Name == "WakeProtectionSwitch");
            protection.SetCurrentValue(ToggleSwitch.IsCheckedProperty, false);
            Require(
                !controls.IsNightLightProtectionEnabled,
                "Wake protection toggle did not update the setting."
            );
            protection.SetCurrentValue(ToggleSwitch.IsCheckedProperty, true);
            Require(
                controls.IsNightLightProtectionEnabled,
                "Wake protection toggle did not enable the setting."
            );
            var shield = Services.BlackoutCover.CreateWindow();
            try
            {
                shield.Width = 240;
                shield.Height = 160;
                shield.Position = window.Position + new PixelPoint(40, 40);
                shield.Show();
                await Task.Delay(150);
                Require(
                    !shield.ShowInTaskbar
                        && !shield.ShowActivated
                        && shield.Topmost
                        && shield.WindowDecorations == WindowDecorations.None,
                    "The protection cover is not a borderless, non-activating topmost window."
                );
                using var rendered = new RenderTargetBitmap(
                    new PixelSize(240, 160),
                    new Vector(96, 96)
                );
                rendered.Render(shield);
                rendered.Save(
                    Path.Combine(directory, "blackout.png"),
                    PngBitmapEncoderOptions.Default
                );
                using var copy = new WriteableBitmap(
                    new PixelSize(240, 160),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Opaque
                );
                using var pixels = copy.Lock();
                rendered.CopyPixels(pixels);
                var bytes = new byte[pixels.RowBytes * 160];
                Marshal.Copy(pixels.Address, bytes, 0, bytes.Length);
                for (var y = 0; y < 160; y++)
                for (var x = 0; x < 240; x++)
                {
                    var offset = y * pixels.RowBytes + x * 4;
                    Require(
                        bytes[offset] == 0 && bytes[offset + 1] == 0 && bytes[offset + 2] == 0,
                        "The protection cover rendered a nonblack pixel."
                    );
                }
            }
            finally
            {
                shield.Close();
            }
            controls.SaveNow();
            File.WriteAllText(
                Path.Combine(directory, "verification.txt"),
                "PASS: compiled preset button binding; per-monitor isolation; schedule generation; invalid time validation; manual preset with invalid schedule; correction; bound row deletion; custom preset save/delete; switching monitors; settings save; wake protection toggle; opaque black cover.\n"
            );
            App.Shutdown();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "verification.txt"), ex.ToString());
            App.Shutdown(1);
        }
    }

    private static void Capture(Window window, string directory, string name)
    {
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height),
            new Vector(96, 96)
        );
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
