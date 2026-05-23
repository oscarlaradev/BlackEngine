using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace BlackEngine.Controls;

public class ColorWheelControl : Control
{
    // Propiedad de dependencia para enlazar el color seleccionado (TwoWay)
    public static readonly StyledProperty<Color> SelectedColorProperty =
        AvaloniaProperty.Register<ColorWheelControl, Color>(
            nameof(SelectedColor),
            Colors.White,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private struct SpherePoint
    {
        public double X;
        public double Y;
        public double Z;
        public Color Color;
    }

    private readonly List<SpherePoint> _points = new();
    private double _rx = 0.5; // Ángulo Pitch inicial
    private double _ry = 0.5; // Ángulo Yaw inicial
    
    private Point _lastMousePos;
    private bool _isDragging;

    public ColorWheelControl()
    {
        ClipToBounds = false;
        GenerateSpherePoints();
    }

    private void GenerateSpherePoints()
    {
        _points.Clear();
        int numPoints = 750; // Densidad premium de partículas
        var goldenRatio = (1.0 + Math.Sqrt(5.0)) / 2.0;

        for (int i = 0; i < numPoints; i++)
        {
            double theta = 2.0 * Math.PI * i / goldenRatio;
            double phi = Math.Asin(-1.0 + 2.0 * i / (double)numPoints);

            // Coordenadas esféricas unitarias
            double x = Math.Cos(phi) * Math.Cos(theta);
            double y = Math.Sin(phi);
            double z = Math.Cos(phi) * Math.Sin(theta);

            // Calcular HSL correspondiente
            // Hue es la longitud theta (0..360)
            double hue = (theta * 180.0 / Math.PI) % 360.0;
            if (hue < 0) hue += 360.0;

            // Saturation es la distancia del eje central (y)
            double sat = Math.Cos(phi);

            // Lightness va del polo sur (-1 -> negro) al polo norte (1 -> blanco)
            double light = (y + 1.0) / 2.0;

            Color color = HslToRgb(hue, sat, light);

            _points.Add(new SpherePoint { X = x, Y = y, Z = z, Color = color });
        }
    }

    private static Color HslToRgb(double h, double s, double l)
    {
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l; // acromático (escala de grises)
        }
        else
        {
            double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p = 2.0 * l - q;
            r = HueToRgb(p, q, h / 360.0 + 1.0 / 3.0);
            g = HueToRgb(p, q, h / 360.0);
            b = HueToRgb(p, q, h / 360.0 - 1.0 / 3.0);
        }
        return Color.FromRgb((byte)Math.Clamp(r * 255.0, 0, 255), (byte)Math.Clamp(g * 255.0, 0, 255), (byte)Math.Clamp(b * 255.0, 0, 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1.0;
        if (t > 1) t -= 1.0;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _lastMousePos = e.GetPosition(this);
        _isDragging = true;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging)
        {
            var currentPos = e.GetPosition(this);
            double dx = currentPos.X - _lastMousePos.X;
            double dy = currentPos.Y - _lastMousePos.Y;

            // Rotar la esfera según el arrastre
            _ry += dx * 0.007; // Yaw
            _rx -= dy * 0.007; // Pitch

            // Limitar el cabeceo (Pitch) para evitar giros imposibles
            _rx = Math.Clamp(_rx, -Math.PI / 2.0 + 0.1, Math.PI / 2.0 - 0.1);

            _lastMousePos = currentPos;
            InvalidateVisual(); // Redibujar a 60 FPS
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);

            // Si el movimiento fue pequeño, procesarlo como un clic de selección
            var endPos = e.GetPosition(this);
            double dx = endPos.X - _lastMousePos.X;
            double dy = endPos.Y - _lastMousePos.Y;
            
            // Si el mouse casi no se movió, es un clic de selección
            SelectColorAtPoint(endPos);
        }
    }

    private void SelectColorAtPoint(Point clickPoint)
    {
        double radius = Math.Min(Bounds.Width, Bounds.Height) * 0.4;
        double centerX = Bounds.Width / 2.0;
        double centerY = Bounds.Height / 2.0;

        double minDist = double.MaxValue;
        Color bestColor = Colors.White;

        foreach (var p in _points)
        {
            // Rotar Y
            double x1 = p.X * Math.Cos(_ry) - p.Z * Math.Sin(_ry);
            double z1 = p.X * Math.Sin(_ry) + p.Z * Math.Cos(_ry);

            // Rotar X
            double y2 = p.Y * Math.Cos(_rx) - z1 * Math.Sin(_rx);
            double z2 = p.Y * Math.Sin(_rx) + z1 * Math.Cos(_rx);

            // Solo puntos frontales orientados al espectador (Z >= 0)
            if (z2 < 0) continue;

            double sx = centerX + x1 * radius;
            double sy = centerY - y2 * radius;

            double dx = sx - clickPoint.X;
            double dy = sy - clickPoint.Y;
            double dist = dx * dx + dy * dy;

            if (dist < minDist)
            {
                minDist = dist;
                bestColor = p.Color;
            }
        }

        // Selección si está en una vecindad tolerable (máximo 25px)
        if (minDist < 625.0)
        {
            SelectedColor = bestColor;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        double radius = Math.Min(width, height) * 0.4;
        double centerX = width / 2.0;
        double centerY = height / 2.0;

        // Dibujar halo del contorno de la esfera (Gris ultra fino)
        var haloPen = new Pen(new SolidColorBrush(Color.Parse("#222222")), 1.0);
        context.DrawEllipse(null, haloPen, new Point(centerX, centerY), radius, radius);

        // Proyectar todos los puntos tridimensionales
        var projected = new List<(double sx, double sy, double sz, Color color)>();
        foreach (var p in _points)
        {
            // Rotar Y (Yaw)
            double x1 = p.X * Math.Cos(_ry) - p.Z * Math.Sin(_ry);
            double z1 = p.X * Math.Sin(_ry) + p.Z * Math.Cos(_ry);

            // Rotar X (Pitch)
            double y2 = p.Y * Math.Cos(_rx) - z1 * Math.Sin(_rx);
            double z2 = p.Y * Math.Sin(_rx) + z1 * Math.Cos(_rx);

            double sx = centerX + x1 * radius;
            double sy = centerY - y2 * radius; // Invertir eje Y para pantalla

            projected.Add((sx, sy, z2, p.Color));
        }

        // Algoritmo del Pintor (ordenar por profundidad Z ascendente)
        // Dibujamos de atrás hacia adelante
        var sorted = projected.OrderBy(pt => pt.sz).ToList();

        foreach (var pt in sorted)
        {
            // Mapear opacidad en base a profundidad (Z: -1 atrás, 1 adelante)
            double depthOpacity = (pt.sz + 1.2) / 2.2;
            depthOpacity = Math.Clamp(depthOpacity, 0.15, 1.0);

            // Puntos más cercanos son más grandes para dar perspectiva
            double ptRadius = 2.0 + depthOpacity * 3.5;

            var brush = new SolidColorBrush(pt.color, depthOpacity);
            context.DrawGeometry(brush, null, new EllipseGeometry(new Rect(pt.sx - ptRadius, pt.sy - ptRadius, ptRadius * 2.0, ptRadius * 2.0)));
        }

        // Dibujar indicador en el centro con el color seleccionado actualmente
        var selColorBrush = new SolidColorBrush(SelectedColor);
        var selColorPen = new Pen(new SolidColorBrush(Colors.White), 1.5);
        context.DrawEllipse(selColorBrush, selColorPen, new Point(centerX, centerY), 8.0, 8.0);
    }
}
