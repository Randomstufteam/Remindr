using System.Windows.Controls;

namespace Remindr.Setup;

public partial class LogoMark : UserControl
{
    public LogoMark()
    {
        InitializeComponent();
        Loaded += (_, _) => Motion.Pop(this, 0.8);
    }
}
