using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using ActiMetrics.Service.Services;

public class Ticket : Form
{
    private Panel btnApps;
    private Panel btnFisico;
    private TextBox txtDescripcion;
    private Button btnEnviar;
    private ProgressBar spinner;
    private string _categoriaSeleccionada = "";

    // ← Para poder arrastrar el formulario sin barra de título
    private Point _dragOffset;

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    private readonly SyncService _syncService;

    public Ticket(SyncService syncService)
    {

        _syncService = syncService;
        this.Text = "Nueva Solicitud";
        this.Size = new Size(460, 580);
        this.BackColor = Color.FromArgb(26, 26, 46);
        this.FormBorderStyle = FormBorderStyle.None; // ← sin barra de título
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MaximizeBox = false;
        this.Opacity = 0; // ← empieza invisible para la animación
        this.Region = RoundedRegion(this.Width, this.Height, 16);

        // Arrastrar la ventana haciendo click en cualquier parte
        this.MouseDown += FormMouseDown;

        // --- Botón cerrar appuserizado ---
        var btnCerrar = new Button();
        btnCerrar.Text = "✕";
        btnCerrar.Size = new Size(32, 32);
        btnCerrar.Location = new Point(this.Width - 42, 10);
        btnCerrar.FlatStyle = FlatStyle.Flat;
        btnCerrar.FlatAppearance.BorderSize = 0;
        btnCerrar.BackColor = Color.Transparent;
        btnCerrar.ForeColor = Color.FromArgb(180, 180, 200);
        btnCerrar.Font = new Font("Segoe UI", 11);
        btnCerrar.Cursor = Cursors.Hand;
        btnCerrar.Click += (s, e) => this.Close();
        btnCerrar.MouseEnter += (s, e) => btnCerrar.ForeColor = Color.Black;
        btnCerrar.MouseLeave += (s, e) => btnCerrar.ForeColor = Color.FromArgb(180, 180, 200);

        // --- Título ---
        var lblTitulo = new Label();
        lblTitulo.Text = "¿De qué se trata tu solicitud?";
        lblTitulo.ForeColor = Color.White;
        lblTitulo.Font = new Font("Segoe UI", 13, FontStyle.Bold);
        lblTitulo.TextAlign = ContentAlignment.MiddleCenter;
        lblTitulo.Location = new Point(20, 25);
        lblTitulo.Size = new Size(400, 35);
        lblTitulo.MouseDown += FormMouseDown; // también arrastra desde el título


        // --- Botones categoría ---
        // ← pasa la categoria directamente, sin Click externo
        btnApps = CrearBotoCategoria("⊞\nAplicaciones del\ncomputador", 20, 125, false, "aplicaciones");
        btnFisico = CrearBotoCategoria("🖥\nProblemas\nfísicos", 225, 125, false, "equipo");

        // --- TextBox descripción ---
        txtDescripcion = new TextBox();
        txtDescripcion.Multiline = true;
        txtDescripcion.PlaceholderText = "Coloca una descripción detallada de tu solicitud";
        txtDescripcion.Location = new Point(20, 265);
        txtDescripcion.Size = new Size(400, 100);
        txtDescripcion.BackColor = Color.FromArgb(30, 30, 63);
        txtDescripcion.ForeColor = Color.White;
        txtDescripcion.Font = new Font("Segoe UI", 10);
        txtDescripcion.BorderStyle = BorderStyle.FixedSingle;


        // --- Botón Enviar ---
        btnEnviar = new Button();
        btnEnviar.Text = "Enviar";
        btnEnviar.Location = new Point(20, 445);
        btnEnviar.Size = new Size(400, 44);
        btnEnviar.BackColor = Color.FromArgb(37, 99, 235);
        btnEnviar.ForeColor = Color.White;
        btnEnviar.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        btnEnviar.FlatStyle = FlatStyle.Flat;
        btnEnviar.FlatAppearance.BorderSize = 0;
        btnEnviar.Cursor = Cursors.Hand;
        btnEnviar.Click += BtnEnviar_Click;

        // --- Spinner (barra indeterminada) mostrada mientras se envía ---
        spinner = new ProgressBar();
        spinner.Style = ProgressBarStyle.Marquee;
        spinner.MarqueeAnimationSpeed = 30;
        spinner.Location = new Point(20, 500);
        spinner.Size = new Size(400, 6);
        spinner.Visible = false;

        this.Controls.AddRange(new Control[] {
            btnCerrar, lblTitulo,
            btnApps, btnFisico,
            txtDescripcion, btnEnviar, spinner
        });
    }

    // ← Animación fade-in al mostrarse
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FadeIn();
    }

    private void FadeIn()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 15 };
        timer.Tick += (s, e) =>
        {
            this.Opacity += 0.07;
            if (this.Opacity >= 1)
            {
                this.Opacity = 1;
                timer!.Stop();
                timer.Dispose();
            }
        };
        timer.Start();
    }

    // ← Permite arrastrar la ventana
    private void FormMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(this.Handle, 0xA1, 0x2, 0); // WM_NCLBUTTONDOWN
        }
    }

    private static Region RoundedRegion(int width, int height, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
        path.AddArc(width - radius * 2, 0, radius * 2, radius * 2, 270, 90);
        path.AddArc(width - radius * 2, height - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(0, height - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }

    private Panel CrearBotoCategoria(string texto, int x, int y, bool activo, string categoria)
    {
        int panelW = 195, panelH = 120;

        var panel = new Panel();
        panel.Location = new Point(x, y);
        panel.Size = new Size(panelW, panelH);
        panel.BackColor = activo ? Color.FromArgb(37, 99, 235) : Color.FromArgb(30, 30, 63);
        panel.Cursor = Cursors.Hand;
        panel.Region = RoundedRegion(panelW, panelH, 12);

        var lbl = new Label();
        lbl.Text = texto;
        lbl.ForeColor = activo ? Color.White : Color.FromArgb(200, 200, 200);
        lbl.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        lbl.TextAlign = ContentAlignment.MiddleCenter;
        lbl.Dock = DockStyle.Fill;
        lbl.Cursor = Cursors.Hand;
        // ← tanto label como panel llaman directo a SeleccionarCategoria
        lbl.Click += (s, e) => SeleccionarCategoria(categoria);
        panel.Click += (s, e) => SeleccionarCategoria(categoria);

        panel.Controls.Add(lbl);
        return panel;
    }

    private void SeleccionarCategoria(string categoria)
    {
        _categoriaSeleccionada = categoria;

        // Resetea ambos primero
        btnApps.BackColor = Color.FromArgb(30, 30, 63);
        btnFisico.BackColor = Color.FromArgb(30, 30, 63);
        ((Label)btnApps.Controls[0]).ForeColor = Color.FromArgb(200, 200, 200);
        ((Label)btnFisico.Controls[0]).ForeColor = Color.FromArgb(200, 200, 200);

        // Activa solo el seleccionado
        switch (categoria)
        {
            case "aplicaciones":
                btnApps.BackColor = Color.FromArgb(37, 99, 235);
                ((Label)btnApps.Controls[0]).ForeColor = Color.White;
                break;
            case "equipo":
                btnFisico.BackColor = Color.FromArgb(37, 99, 235);
                ((Label)btnFisico.Controls[0]).ForeColor = Color.White;
                break;
            default:
                // ninguno activo, ambos ya reseteados arriba
                _categoriaSeleccionada = "";
                break;
        }
    }

    private async void BtnEnviar_Click(object sender, EventArgs e)
    {
        // Evita doble envío mientras la petición está en curso
        btnEnviar.Enabled = false;
        btnEnviar.Text = "Enviando…";
        btnEnviar.BackColor = Color.FromArgb(60, 80, 150);
        spinner.Visible = true;

        try
        {
            // SaveTicketAsync ya no lanza por un status no-2xx: el ticket queda encolado
            // localmente y se reintenta solo aunque el envío en línea falle (ver SyncService).
            var result = await _syncService.SaveTicketAsync(_categoriaSeleccionada, txtDescripcion.Text);
            MessageBox.Show(result.Message, "Ticket", MessageBoxButtons.OK,
                result.Sent ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            this.Close();
            return;
        }
        catch (Exception ex)
        {
            // Solo llega acá si algo falla ANTES de encolar (ej. no se pudo escribir en la BD
            // local) — el envío en sí ya no propaga excepciones al llamador.
            MessageBox.Show($"Error al enviar: {ex.Message}");
        }
        finally
        {
            // Restaura el botón solo si el formulario sigue abierto (envío fallido)
            if (!this.IsDisposed && !this.Disposing)
            {
                spinner.Visible = false;
                btnEnviar.Enabled = true;
                btnEnviar.Text = "Enviar";
                btnEnviar.BackColor = Color.FromArgb(37, 99, 235);
            }
        }
    }
}