namespace SlickDirectory;

public class TransparentForm : Form
{
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    public TransparentForm()
    {
        // Make the form invisible
        this.Opacity = 0;
        this.ShowInTaskbar = false;

        // Prevent the form from showing
        this.Load += (sender, e) => { this.Hide(); };
    }
}