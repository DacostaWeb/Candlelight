using System.Net.Http;
using System.Text.Json;
using Candlelight.Engine;

namespace Candlelight.Next;

internal sealed class LocationDialog : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill, AccessibleName = "Cidade" };
    private readonly ListBox _results = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        AccessibleName = "Cidades encontradas",
    };
    private readonly NumericUpDown _latitude = new()
    {
        Minimum = -90,
        Maximum = 90,
        DecimalPlaces = 4,
        Width = 115,
        AccessibleName = "Latitude",
    };
    private readonly NumericUpDown _longitude = new()
    {
        Minimum = -180,
        Maximum = 180,
        DecimalPlaces = 4,
        Width = 115,
        AccessibleName = "Longitude",
    };
    private readonly ComboBox _zone = new DarkComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill,
        AccessibleName = "Fuso horário",
    };
    private readonly Label _message = LightTheme.Label(
        "A pesquisa usa Open-Meteo / GeoNames. Os horários são calculados no PC.",
        9,
        true
    );
    private readonly CancellationTokenSource _closed = new();
    private SolarLocation? _chosen;
    private bool _settingLocation;
    private bool _coordinatesEdited;
    private bool _searching;
    internal SolarLocation? SelectedLocation { get; private set; }

    internal LocationDialog(SolarLocation? current)
    {
        Text = "Nascer e pôr do sol";
        Font = new("Segoe UI", 10);
        BackColor = LightTheme.Background;
        ForeColor = LightTheme.Text;
        ClientSize = new(570, 430);
        MinimumSize = new(550, 440);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        Icon = new Icon(Path.Combine(System.AppContext.BaseDirectory, "favicon.ico"));
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new(22),
            RowCount = 7,
            ColumnCount = 1,
        };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var h in new[] { 38, 39, -1, 44, 38, 43, 40 })
            root.RowStyles.Add(new(h < 0 ? SizeType.Percent : SizeType.Absolute, h < 0 ? 100 : h));
        root.Controls.Add(LightTheme.Label("Uma cidade chega", 17), 0, 0);
        var search = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = new(0),
        };
        search.ColumnStyles.Add(new(SizeType.Percent, 100));
        search.RowCount = 1;
        search.RowStyles.Add(new(SizeType.Percent, 100));
        search.ColumnStyles.Add(new(SizeType.Absolute, 116));
        search.Controls.Add(_name, 0, 0);
        var find = LightTheme.Button("Pesquisar", () => _ = SearchAsync());
        search.Controls.Add(find, 1, 0);
        root.Controls.Add(search, 0, 1);
        root.Controls.Add(_results, 0, 2);
        var coordinates = LightTheme.Row();
        coordinates.Controls.Add(LightTheme.Label("Latitude"));
        coordinates.Controls.Add(_latitude);
        coordinates.Controls.Add(LightTheme.Label("Longitude"));
        coordinates.Controls.Add(_longitude);
        root.Controls.Add(coordinates, 0, 3);
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
            _zone.Items.Add(zone);
        _zone.DisplayMember = nameof(TimeZoneInfo.DisplayName);
        root.Controls.Add(_zone, 0, 4);
        _message.MaximumSize = new(510, 0);
        root.Controls.Add(_message, 0, 5);
        var actions = LightTheme.Row();
        actions.FlowDirection = FlowDirection.RightToLeft;
        var save = LightTheme.Button("Usar localização", Save, true);
        var cancel = LightTheme.Button("Cancelar", () => DialogResult = DialogResult.Cancel);
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 6);
        Controls.Add(root);
        LightTheme.Style(this);
        _results.SelectedIndexChanged += (_, _) =>
        {
            if (_results.SelectedItem is Place place)
                SetLocation(place.Location);
        };
        _name.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                _ = SearchAsync();
                e.SuppressKeyPress = true;
            }
        };
        CancelButton = cancel;
        _latitude.ValueChanged += (_, _) =>
        {
            if (!_settingLocation)
                _coordinatesEdited = true;
        };
        _longitude.ValueChanged += (_, _) =>
        {
            if (!_settingLocation)
                _coordinatesEdited = true;
        };
        if (current is not null)
            SetLocation(current);
        else
            _zone.SelectedItem = _zone
                .Items.Cast<TimeZoneInfo>()
                .FirstOrDefault(z => z.Id == TimeZoneInfo.Local.Id);
        FormClosed += (_, _) =>
        {
            _closed.Cancel();
            _closed.Dispose();
        };
        AutoScaleDimensions = new(96, 96);
    }

    private void SetLocation(SolarLocation location)
    {
        _settingLocation = true;
        _chosen = location;
        _coordinatesEdited = false;
        _name.Text = location.Name;
        _latitude.Value = (decimal)location.Latitude;
        _longitude.Value = (decimal)location.Longitude;
        var resolved = TimeZoneInfo.FindSystemTimeZoneById(location.TimeZoneId);
        _zone.SelectedItem = _zone
            .Items.Cast<TimeZoneInfo>()
            .FirstOrDefault(z => z.Id == resolved.Id || z.HasSameRules(resolved));
        _settingLocation = false;
    }

    private async Task SearchAsync()
    {
        if (_searching)
            return;
        if (_name.Text.Trim().Length < 2)
        {
            _message.Text = "Escreve o nome de uma cidade.";
            return;
        }
        var query = _name.Text.Trim();
        _searching = true;
        _message.Text = "A procurar cidades…";
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxResponseContentBufferSize = 65536,
            };
            var uri =
                "https://geocoding-api.open-meteo.com/v1/search?count=6&language=pt&format=json&name="
                + Uri.EscapeDataString(query);
            var body = await client.GetStringAsync(uri, _closed.Token);
            if (IsDisposed)
                return;
            using var document = JsonDocument.Parse(body);
            _results.Items.Clear();
            if (document.RootElement.TryGetProperty("results", out var results))
                foreach (var result in results.EnumerateArray())
                {
                    if (!result.TryGetProperty("timezone", out var zone))
                        continue;
                    var location = new SolarLocation(
                        result.GetProperty("name").GetString()!,
                        result.GetProperty("latitude").GetDouble(),
                        result.GetProperty("longitude").GetDouble(),
                        zone.GetString()!
                    ).Validate();
                    var country = result.TryGetProperty("country", out var c) ? c.GetString() : "";
                    _results.Items.Add(new Place(location, country ?? ""));
                }
            _message.Text =
                _results.Items.Count == 0
                    ? "Sem resultados. Experimenta uma cidade próxima ou introduz as coordenadas."
                    : "Escolhe a cidade. Dados de localização: Open-Meteo / GeoNames.";
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception error)
            when (error
                    is HttpRequestException
                        or TaskCanceledException
                        or JsonException
                        or ArgumentException
                        or TimeZoneNotFoundException
            )
        {
            if (!IsDisposed)
                _message.Text =
                    "A pesquisa não está disponível. Podes preencher as coordenadas e o fuso horário.";
        }
        finally
        {
            _searching = false;
        }
    }

    private void Save()
    {
        try
        {
            if (_chosen?.Name != _name.Text.Trim() && !_coordinatesEdited)
                throw new ArgumentException(
                    "Seleciona uma cidade nos resultados ou introduz as coordenadas."
                );
            if (_zone.SelectedItem is not TimeZoneInfo zone)
                throw new ArgumentException("Escolhe o fuso horário.");
            SelectedLocation = new SolarLocation(
                _name.Text.Trim(),
                (double)_latitude.Value,
                (double)_longitude.Value,
                zone.Id
            ).Validate();
            DialogResult = DialogResult.OK;
        }
        catch (Exception error)
        {
            _message.Text = error.Message;
        }
    }

    internal void VerifyPreviewControls()
    {
        var expected = _chosen ?? throw new InvalidOperationException("Preview location missing.");
        _name.Text = "Cidade ainda não escolhida";
        Save();
        if (SelectedLocation is not null)
            throw new InvalidOperationException("An unselected city kept old coordinates.");
        _name.Text = expected.Name;
        Save();
        if (
            SelectedLocation is null
            || Math.Abs(SelectedLocation.Latitude - expected.Latitude) > .0001
            || !TimeZoneInfo
                .FindSystemTimeZoneById(SelectedLocation.TimeZoneId)
                .HasSameRules(TimeZoneInfo.FindSystemTimeZoneById(expected.TimeZoneId))
        )
            throw new InvalidOperationException("Location/time-zone binding failed.");
    }

    private sealed record Place(SolarLocation Location, string Country)
    {
        public override string ToString() => Location.Name + ", " + Country;
    }
}
