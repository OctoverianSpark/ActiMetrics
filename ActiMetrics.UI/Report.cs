using System.Drawing;
using System.Windows.Forms;
using ActiMetrics.Service.Services;
using ActiMetrics.Shared.Models;

public class Report : Form
{
    private readonly SyncService _syncService;
    private ComboBox cmbTipo;
    private TextBox txtMensaje;
    private Button btnEnviar;
    private ProgressBar spinner;

    public Report(SyncService syncService)
    {
        _syncService = syncService;

        this.Text = "Nuevo Reporte";
        this.Size = new Size(460, 380);
        this.BackColor = Color.FromArgb(26, 26, 46);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        var lblTitulo = new Label
        {
            Text = "¿Qué está pasando?",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 20),
            Size = new Size(400, 35)
        };

        cmbTipo = new ComboBox
        {
            Location = new Point(20, 70),
            Size = new Size(400, 30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(30, 30, 63),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10),
            DisplayMember = nameof(ReportType.Name),
            ValueMember = nameof(ReportType.Id)
        };

        txtMensaje = new TextBox
        {
            Multiline = true,
            PlaceholderText = "Describe el problema con el mayor detalle posible",
            Location = new Point(20, 115),
            Size = new Size(400, 140),
            BackColor = Color.FromArgb(30, 30, 63),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10),
            BorderStyle = BorderStyle.FixedSingle
        };

        btnEnviar = new Button
        {
            Text = "Enviar",
            Location = new Point(20, 275),
            Size = new Size(400, 44),
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnEnviar.FlatAppearance.BorderSize = 0;
        btnEnviar.Click += BtnEnviar_Click;

        spinner = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Location = new Point(20, 325),
            Size = new Size(400, 6),
            Visible = false
        };

        this.Controls.AddRange(new Control[] { lblTitulo, cmbTipo, txtMensaje, btnEnviar, spinner });
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await CargarTiposReporteAsync();
    }

    private async Task CargarTiposReporteAsync()
    {
        cmbTipo.Enabled = false;
        var tipos = await _syncService.GetReportTypesAsync();

        if (tipos is null || tipos.Count == 0)
        {
            MessageBox.Show("No se pudo cargar el catálogo de tipos de reporte.", "Reporte", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            this.Close();
            return;
        }

        cmbTipo.DataSource = tipos;
        cmbTipo.Enabled = true;
    }

    private async void BtnEnviar_Click(object? sender, EventArgs e)
    {
        if (cmbTipo.SelectedValue is not int reportTypeId)
        {
            MessageBox.Show("Selecciona un tipo de reporte.", "Reporte", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(txtMensaje.Text))
        {
            MessageBox.Show("Describe el problema antes de enviar.", "Reporte", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnEnviar.Enabled = false;
        btnEnviar.Text = "Enviando…";
        spinner.Visible = true;

        try
        {
            var response = await _syncService.SendReportAsync(reportTypeId, txtMensaje.Text);
            if (response.IsSuccessStatusCode)
            {
                MessageBox.Show("Reporte enviado correctamente.", "Reporte", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
                return;
            }

            var body = await response.Content.ReadAsStringAsync();
            MessageBox.Show($"Error {response.StatusCode}: {body}", "Reporte", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al enviar: {ex.Message}", "Reporte", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!this.IsDisposed && !this.Disposing)
            {
                spinner.Visible = false;
                btnEnviar.Enabled = true;
                btnEnviar.Text = "Enviar";
            }
        }
    }
}
