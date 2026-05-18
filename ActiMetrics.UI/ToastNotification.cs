using System;
using System.Drawing;
using System.Windows.Forms;

public class ToastNotification : Form
{
    private Label lblTitulo;
    private Label lblMensaje;
    private System.Windows.Forms.Timer timerCierre;
    private int _targetX;
    private int _targetY;

    public ToastNotification(string titulo, string mensaje, int duracionMs = 3000,
        string? actionLabel = null, Action? onAction = null)
    {
        bool hasAction = actionLabel is not null && onAction is not null;

        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.TopMost = true;
        this.BackColor = Color.FromArgb(45, 45, 48);
        this.Width = 600;
        this.Height = hasAction ? 260 : 200;
        this.Opacity = 0;

        // Posición destino: centro de la pantalla
        var pantalla = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        _targetX = (pantalla.Width / 2) - (this.Width / 2);
        _targetY = (pantalla.Height / 2) - (this.Height / 2);

        // Empieza fuera de la pantalla arriba
        this.Location = new Point(_targetX, -this.Height);

        this.Region = RoundedRegion(this.Width, this.Height, 12);

        // Barra de color izquierda
        var barra = new Panel();
        barra.BackColor = Color.DodgerBlue;
        barra.Size = new Size(8, this.Height);
        barra.Location = new Point(0, 0);

        // Título
        lblTitulo = new Label();
        lblTitulo.Text = titulo;
        lblTitulo.ForeColor = Color.White;
        lblTitulo.Font = new Font("Segoe UI", 20, FontStyle.Bold);
        lblTitulo.Location = new Point(25, 30);
        lblTitulo.Size = new Size(560, 30);
        lblTitulo.BackColor = Color.Transparent;

        // Mensaje
        lblMensaje = new Label();
        lblMensaje.Text = mensaje;
        lblMensaje.ForeColor = Color.FromArgb(200, 200, 200);
        lblMensaje.Font = new Font("Segoe UI", 16);
        lblMensaje.Location = new Point(25, 70);
        lblMensaje.Size = new Size(560, 80);
        lblMensaje.BackColor = Color.Transparent;

        this.Controls.AddRange(new Control[] { barra, lblTitulo, lblMensaje });

        // Cerrar al hacer click (excepto sobre el botón de acción)
        this.Click += (s, e) => SlideOut();
        lblTitulo.Click += (s, e) => SlideOut();
        lblMensaje.Click += (s, e) => SlideOut();
        barra.Click += (s, e) => SlideOut();

        if (hasAction)
        {
            var btn = new Button();
            btn.Text = actionLabel;
            btn.Font = new Font("Segoe UI", 13, FontStyle.Bold);
            btn.ForeColor = Color.White;
            btn.BackColor = Color.DodgerBlue;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Size = new Size(200, 42);
            btn.Location = new Point(25, 200);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) =>
            {
                onAction!.Invoke();
                SlideOut();
            };
            this.Controls.Add(btn);
        }

        // Timer para cerrar automáticamente
        timerCierre = new System.Windows.Forms.Timer();
        timerCierre.Interval = duracionMs;
        timerCierre.Tick += (s, e) =>
        {
            timerCierre!.Stop();
            SlideOut();
        };
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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SlideIn();
    }

    private void SlideIn()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 10 };
        timer.Tick += (s, e) =>
        {
            int speed = 18;
            this.Opacity = Math.Min(this.Opacity + 0.08, 1.0);

            if (this.Top < _targetY)
                this.Top = Math.Min(this.Top + speed, _targetY);
            else
            {
                this.Opacity = 1;
                this.Top = _targetY;
                timer!.Stop();
                timer.Dispose();
                timerCierre.Start(); // empieza a contar tras llegar
            }
        };
        timer.Start();
    }

    private void InitializeComponent()
    {

    }

    private void SlideOut()
    {
        timerCierre.Stop();
        var timer = new System.Windows.Forms.Timer { Interval = 10 };
        timer.Tick += (s, e) =>
        {
            int speed = 18;
            this.Top -= speed;
            this.Opacity = Math.Max(this.Opacity - 0.06, 0);

            if (this.Top + this.Height < 0)
            {
                timer!.Stop();
                timer.Dispose();
                this.Close();
            }
        };
        timer.Start();
    }
}