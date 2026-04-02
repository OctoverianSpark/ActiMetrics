using System;
using System.Collections.Generic;
using System.Text;

namespace Tracer.UI
{
    public class LoginCheck : Form
    {
        private TextBox txtEmail;
        private Button btnEntrar;
        public string? Email { get; private set; }


        public LoginCheck()
        {


            this.Text = "ActiMetrics";
            this.Size = new Size(380, 220);
            this.BackColor = Color.FromArgb(26, 26, 46);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Region = RoundedRegion(this.Width, this.Height, 16);

            var lblTitulo = new Label();
            lblTitulo.Text = "Ingresa tu correo";
            lblTitulo.ForeColor = Color.White;
            lblTitulo.Font = new Font("Segoe UI", 13, FontStyle.Bold);
            lblTitulo.TextAlign = ContentAlignment.MiddleCenter;
            lblTitulo.Location = new Point(20, 30);
            lblTitulo.Size = new Size(340, 30);

            txtEmail = new TextBox();
            txtEmail.PlaceholderText = "correo@empresa.com";
            txtEmail.Location = new Point(20, 80);
            txtEmail.Size = new Size(340, 35);
            txtEmail.BackColor = Color.FromArgb(30, 30, 63);
            txtEmail.ForeColor = Color.White;
            txtEmail.Font = new Font("Segoe UI", 11);
            txtEmail.BorderStyle = BorderStyle.FixedSingle;

            btnEntrar = new Button();
            btnEntrar.Text = "Entrar";
            btnEntrar.Location = new Point(20, 130);
            btnEntrar.Size = new Size(340, 42);
            btnEntrar.BackColor = Color.FromArgb(37, 99, 235);
            btnEntrar.ForeColor = Color.White;
            btnEntrar.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            btnEntrar.FlatStyle = FlatStyle.Flat;
            btnEntrar.FlatAppearance.BorderSize = 0;
            btnEntrar.Cursor = Cursors.Hand;
            btnEntrar.Click += BtnEntrar_Click;

            this.Controls.AddRange(new Control[] { lblTitulo, txtEmail, btnEntrar });
        }
        private async void BtnEntrar_Click(object sender, EventArgs e)
        {

            var http = new HttpClient();

            var response = http.GetAsync($"https://tracerapi.asistentevirtualsas.com/validate_email?email={txtEmail.Text}");

            if (string.IsNullOrWhiteSpace(txtEmail.Text) || !txtEmail.Text.Contains("@"))
            {
                MessageBox.Show("Ingresa un correo válido");
                return;
            }
            Email = txtEmail.Text.Trim();
            this.DialogResult = DialogResult.OK;
            this.Close();
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
    }
}
