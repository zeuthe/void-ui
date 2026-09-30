using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using VoidUI.Models;

namespace VoidUI.Services
{
    public class CrosshairRenderer
    {
        private readonly Canvas _canvas;
        private FrameworkElement _currentElement;
        private Storyboard _animStoryboard;
        private Storyboard _motionStoryboard;
        private readonly List<SolidColorBrush> _rainbowBrushes = new();

        public CrosshairRenderer(Canvas canvas)
        {
            _canvas = canvas;
        }

        public void Render(CrosshairState state)
        {
            _canvas.Children.Clear();
            _animStoryboard?.Stop();
            _animStoryboard = null;
            _motionStoryboard?.Stop();
            _motionStoryboard = null;
            _canvas.RenderTransform = null;
            _rainbowBrushes.Clear();

            double canvasW = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : SystemParameters.PrimaryScreenWidth;
            double canvasH = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : SystemParameters.PrimaryScreenHeight;
            double cx = canvasW / 2.0 + state.PosX;
            double cy = canvasH / 2.0 + state.PosY;

            _currentElement = state.Preset switch
            {
                "dot" => CreateDot(state),
                "cross" => CreateCross(state),
                "circle" => CreateCircle(state),
                "bracket" => CreateBracket(state),
                "tactical" => CreateTactical(state),
                "symbol" => CreateSymbol(state),
                "custom" => CreateSymbol(state),
                _ => CreateDot(state)
            };

            if (_currentElement == null) return;

            if (_currentElement is Canvas cvs)
            {
                Canvas.SetLeft(_currentElement, cx - cvs.Width / 2.0);
                Canvas.SetTop(_currentElement, cy - cvs.Height / 2.0);
            }
            else if (_currentElement is Ellipse el)
            {
                Canvas.SetLeft(_currentElement, cx - el.Width / 2.0);
                Canvas.SetTop(_currentElement, cy - el.Height / 2.0);
            }
            else if (_currentElement is TextBlock tb)
            {
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var tbw = tb.DesiredSize.Width;
                var tbh = tb.DesiredSize.Height;
                Canvas.SetLeft(_currentElement, cx - tbw / 2.0);
                Canvas.SetTop(_currentElement, cy - tbh / 2.0);
            }
            else
            {
                Canvas.SetLeft(_currentElement, cx);
                Canvas.SetTop(_currentElement, cy);
            }

            _canvas.Children.Add(_currentElement);

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new RotateTransform(state.Rotation, cx, cy));
            _canvas.RenderTransform = transformGroup;
            _canvas.RenderTransformOrigin = new Point(0, 0);

            ApplyAnimations(state, cx, cy);
            ApplyMotions(state, cx, cy);
            if (state.RainbowEnabled && state.RainbowPalette.Count >= 2)
                ApplyRainbow(state);
        }

        public void Clear()
        {
            _animStoryboard?.Stop();
            _motionStoryboard?.Stop();
            _canvas.Children.Clear();
            _canvas.RenderTransform = null;
            _rainbowBrushes.Clear();
        }

        private FrameworkElement CreateDot(CrosshairState state)
        {
            var brush = GetBrush(state.Color);
            _rainbowBrushes.Add(brush);
            return new Ellipse
            {
                Width = state.Size,
                Height = state.Size,
                Fill = brush,
                Opacity = state.Opacity / 100.0
            };
        }

        private FrameworkElement CreateCross(CrosshairState state)
        {
            double center = 50.0;
            var c = new Canvas { Width = 100, Height = 100 };
            var hBrush = GetBrush(state.Color);
            var vBrush = GetBrush(state.Color);
            _rainbowBrushes.Add(hBrush);
            _rainbowBrushes.Add(vBrush);
            var hBar = new Rectangle
            {
                Width = state.Size * 2,
                Height = state.Thickness,
                Fill = hBrush,
                Opacity = state.Opacity / 100.0
            };
            Canvas.SetLeft(hBar, center - state.Size);
            Canvas.SetTop(hBar, center - state.Thickness / 2.0);

            var vBar = new Rectangle
            {
                Width = state.Thickness,
                Height = state.Size * 2,
                Fill = vBrush,
                Opacity = state.Opacity / 100.0
            };
            Canvas.SetLeft(vBar, center - state.Thickness / 2.0);
            Canvas.SetTop(vBar, center - state.Size);

            c.Children.Add(hBar);
            c.Children.Add(vBar);
            return c;
        }

        private FrameworkElement CreateCircle(CrosshairState state)
        {
            var brush = GetBrush(state.Color);
            _rainbowBrushes.Add(brush);
            return new Ellipse
            {
                Width = state.Size * 2,
                Height = state.Size * 2,
                Stroke = brush,
                StrokeThickness = state.Thickness,
                Opacity = state.Opacity / 100.0
            };
        }

        private FrameworkElement CreateBracket(CrosshairState state)
        {
            double center = 50.0;
            var c = new Canvas { Width = 100, Height = 100 };
            double s = state.Size * 0.5;
            double t = state.Thickness;
            var brush = GetBrush(state.Color);
            _rainbowBrushes.Add(brush);
            double o = state.Opacity / 100.0;

            var lines = new Line[]
            {
                new Line { X1 = center - s, Y1 = center - s, X2 = center - s * 0.3, Y2 = center - s, Stroke = brush, StrokeThickness = t, Opacity = o },
                new Line { X1 = center + s * 0.3, Y1 = center - s, X2 = center + s, Y2 = center - s, Stroke = brush, StrokeThickness = t, Opacity = o },
                new Line { X1 = center - s, Y1 = center + s, X2 = center - s * 0.3, Y2 = center + s, Stroke = brush, StrokeThickness = t, Opacity = o },
                new Line { X1 = center + s * 0.3, Y1 = center + s, X2 = center + s, Y2 = center + s, Stroke = brush, StrokeThickness = t, Opacity = o },
                new Line { X1 = center - s, Y1 = center - s, X2 = center - s, Y2 = center - s * 0.3, Stroke = brush, StrokeThickness = t, Opacity = o },
                new Line { X1 = center - s, Y1 = center + s * 0.3, X2 = center - s, Y2 = center + s, Stroke = brush, StrokeThickness = t, Opacity = o },
                new Line { X1 = center + s, Y1 = center - s, X2 = center + s, Y2 = center - s * 0.3, Stroke = brush, StrokeThickness = t, Opacity = o },
                new Line { X1 = center + s, Y1 = center + s * 0.3, X2 = center + s, Y2 = center + s, Stroke = brush, StrokeThickness = t, Opacity = o }
            };

            foreach (var line in lines) c.Children.Add(line);
            return c;
        }

        private FrameworkElement CreateTactical(CrosshairState state)
        {
            double center = 50.0;
            var c = new Canvas { Width = 100, Height = 100 };
            double t = state.Thickness;
            var brush = GetBrush(state.Color);
            _rainbowBrushes.Add(brush);

            var vBar = new Rectangle
            {
                Width = t, Height = state.Size,
                Fill = brush, Opacity = state.Opacity / 100.0,
                RadiusX = t / 2.0, RadiusY = t / 2.0
            };
            Canvas.SetLeft(vBar, center - t / 2.0);
            Canvas.SetTop(vBar, center - state.Size * 0.4);

            var hBar = new Rectangle
            {
                Height = t, Width = state.Size,
                Fill = brush, Opacity = state.Opacity / 100.0,
                RadiusX = t / 2.0, RadiusY = t / 2.0
            };
            Canvas.SetLeft(hBar, center - state.Size * 0.4);
            Canvas.SetTop(hBar, center - t / 2.0);

            c.Children.Add(vBar);
            c.Children.Add(hBar);
            return c;
        }

        private FrameworkElement CreateSymbol(CrosshairState state)
        {
            var brush = GetBrush(state.Color);
            _rainbowBrushes.Add(brush);
            return new TextBlock
            {
                Text = state.Symbol,
                FontSize = state.Size * 2.5,
                Foreground = brush,
                Opacity = state.Opacity / 100.0,
                TextAlignment = TextAlignment.Center
            };
        }

        private void ApplyAnimations(CrosshairState state, double cx, double cy)
        {
            _animStoryboard = new Storyboard();
            var duration = new Duration(TimeSpan.FromSeconds(2.0 / Math.Max(state.Speed, 1) * 5.0));

            if (_currentElement is FrameworkElement fe)
            {
                fe.RenderTransformOrigin = new Point(0.5, 0.5);
                var tg = new TransformGroup();
                fe.RenderTransform = tg;

                switch (state.Animation)
                {
                    case "pulse":
                        AddScaleAnimation(fe, tg, duration, 1.0, 1.15);
                        break;
                    case "scale":
                        AddScaleAnimation(fe, tg, new Duration(TimeSpan.FromSeconds(1.5)), 1.0, 1.5);
                        break;
                    case "glow":
                        AddOpacityAnimation(fe, duration, state.Opacity / 100.0, (state.Opacity / 100.0) * 0.7);
                        break;
                }
            }

            if (_animStoryboard.Children.Count > 0)
            {
                _animStoryboard.RepeatBehavior = RepeatBehavior.Forever;
                _animStoryboard.Begin(_canvas);
            }
        }

        private void ApplyMotions(CrosshairState state, double cx, double cy)
        {
            _motionStoryboard = new Storyboard();

            var existingTg = _canvas.RenderTransform as TransformGroup;
            if (existingTg == null)
            {
                existingTg = new TransformGroup();
                _canvas.RenderTransform = existingTg;
            }

            int transformIdx = existingTg.Children.Count;

            if (state.SpinEnabled && _currentElement != null)
            {
                var spinDur = new Duration(TimeSpan.FromSeconds(2.0 / Math.Max(state.SpinSpeed, 1) * 5.0));
                var spinAnim = new DoubleAnimation(0, 360, spinDur)
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };

                if (state.SpinDir == "reverse")
                    spinAnim.SpeedRatio = -1;

                var feTg = _currentElement.RenderTransform as TransformGroup;
                if (feTg == null)
                {
                    feTg = new TransformGroup();
                    _currentElement.RenderTransform = feTg;
                }
                var rotateTransform = new RotateTransform(0);
                feTg.Children.Add(rotateTransform);

                _motionStoryboard.Children.Add(spinAnim);
                Storyboard.SetTarget(spinAnim, _currentElement);
                Storyboard.SetTargetProperty(spinAnim, new PropertyPath(
                    $"(UIElement.RenderTransform).(TransformGroup.Children)[{feTg.Children.Count - 1}].(RotateTransform.Angle)"));
            }

            if (state.OrbitEnabled)
            {
                var orbitDur = new Duration(TimeSpan.FromSeconds(2.0 / Math.Max(state.OrbitSpeed, 1) * 5.0));
                double radius = state.OrbitRadius;

                var orbitAngleAnim = new DoubleAnimation(0, 360, orbitDur)
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };

                if (state.OrbitDir == "reverse")
                    orbitAngleAnim.SpeedRatio = -1;

                if (_currentElement != null)
                {
                    var orbitTransform = new RotateTransform(0, cx, cy);
                    existingTg.Children.Add(orbitTransform);

                    var feTg = _currentElement.RenderTransform as TransformGroup;
                    if (feTg == null)
                    {
                        feTg = new TransformGroup();
                        _currentElement.RenderTransform = feTg;
                    }
                    feTg.Children.Add(new TranslateTransform(radius, 0));

                    _motionStoryboard.Children.Add(orbitAngleAnim);
                    Storyboard.SetTarget(orbitAngleAnim, _canvas);
                    Storyboard.SetTargetProperty(orbitAngleAnim, new PropertyPath(
                        $"(UIElement.RenderTransform).(TransformGroup.Children)[{transformIdx}].(RotateTransform.Angle)"));
                }
            }

            if (_motionStoryboard.Children.Count > 0)
            {
                _motionStoryboard.RepeatBehavior = RepeatBehavior.Forever;
                _motionStoryboard.Begin(_canvas);
            }
        }

        private void ApplyRainbow(CrosshairState state)
        {
            double speed = Math.Max(state.RainbowSpeed, 1);
            var palette = state.RainbowPalette;
            int count = palette.Count;
            var colors = palette.Select(hex => (Color)ColorConverter.ConvertFromString(hex)).ToList();
            double cycleDur = 3.0 / speed;
            var totalDur = new Duration(TimeSpan.FromSeconds(cycleDur * count));

            foreach (var brush in _rainbowBrushes)
            {
                if (brush == null) continue;
                var ca = new ColorAnimationUsingKeyFrames();
                ca.Duration = totalDur;
                ca.RepeatBehavior = RepeatBehavior.Forever;

                for (int i = 0; i < colors.Count; i++)
                {
                    var color = colors[i];
                    double pct = (double)i / colors.Count;
                    ca.KeyFrames.Add(new LinearColorKeyFrame(color, KeyTime.FromPercent(pct)));
                }

                brush.BeginAnimation(SolidColorBrush.ColorProperty, ca);
            }
        }

        private static SolidColorBrush GetBrush(string hex)
        {
            if (string.IsNullOrEmpty(hex)) hex = "#00ff00";
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.Green); }
        }

        private void AddScaleAnimation(FrameworkElement element, TransformGroup tg, Duration duration, double from, double to)
        {
            var scale = new ScaleTransform(1, 1);
            tg.Children.Add(scale);
            int idx = tg.Children.Count - 1;

            var animX = new DoubleAnimation(from, to, duration) { AutoReverse = true };
            Storyboard.SetTarget(animX, element);
            Storyboard.SetTargetProperty(animX, new PropertyPath(
                $"(UIElement.RenderTransform).(TransformGroup.Children)[{idx}].(ScaleTransform.ScaleX)"));
            _animStoryboard.Children.Add(animX);

            var animY = new DoubleAnimation(from, to, duration) { AutoReverse = true };
            Storyboard.SetTarget(animY, element);
            Storyboard.SetTargetProperty(animY, new PropertyPath(
                $"(UIElement.RenderTransform).(TransformGroup.Children)[{idx}].(ScaleTransform.ScaleY)"));
            _animStoryboard.Children.Add(animY);
        }

        private void AddOpacityAnimation(FrameworkElement element, Duration duration, double from, double to)
        {
            var anim = new DoubleAnimation(from, to, duration) { AutoReverse = true };
            Storyboard.SetTarget(anim, element);
            Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));
            _animStoryboard.Children.Add(anim);
        }
    }
}
