using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace SX3_SCANER.View
{
    public partial class AskPasswordWD : Window
    {
        private List<string> _listpass = new List<string>() { "admin" };
        public AskPasswordWD()
        {
            InitializeComponent();
            Passbox.Focus();
        }

        private void Passbox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_listpass.Contains(Passbox.Password.ToLower()))
                {
                    this.DialogResult = true;
                    this.Close();
                }
                else
                {
                    SX3_SCANER.Helper.ProfessionalMessageBox.Show("Incorrect password.");
                    this.DialogResult = false;

                }
            }
            if (e.Key == Key.Escape)
            {
                this.DialogResult = false;
                this.Close();
            }
        }
    }
}
