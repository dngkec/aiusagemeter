// The project sets UseWindowsForms for the tray icon, which drags System.Drawing and
// System.Windows.Forms into the implicit usings. A dozen of their type names collide with WPF's.
// Resolved once here rather than at the top of every file.

global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Color = System.Windows.Media.Color;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using Colors = System.Windows.Media.Colors;
global using Cursors = System.Windows.Input.Cursors;
global using FlowDirection = System.Windows.FlowDirection;
global using FontFamily = System.Windows.Media.FontFamily;
global using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Panel = System.Windows.Controls.Panel;
global using Pen = System.Windows.Media.Pen;
global using Point = System.Windows.Point;
global using Rect = System.Windows.Rect;
global using Size = System.Windows.Size;
global using SolidColorBrush = System.Windows.Media.SolidColorBrush;
global using Vector = System.Windows.Vector;
