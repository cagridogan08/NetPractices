using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace VisaulStateTreeViewTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Create sample data
            var viewModel = new VisualStateViewModel();
            viewModel.CreateSampleData();

            // Set the DataContext
            this.DataContext = viewModel;
        }
    }

    public class VisualStateViewModel
    {
        public ObservableCollection<VisualStateGroup> VisualStateGroups { get; set; }

        public VisualStateViewModel()
        {
            VisualStateGroups = new ObservableCollection<VisualStateGroup>();
        }

        public void CreateSampleData()
        {
            // Create sample visual state groups
            var commonStates = new VisualStateGroup { Name = "CommonStates" };
            var focusStates = new VisualStateGroup { Name = "FocusStates" };

            // Create states for CommonStates
            var normalState = new VisualState { Name = "Normal" };
            var mouseOverState = new VisualState { Name = "MouseOver" };
            var pressedState = new VisualState { Name = "Pressed" };
            var disabledState = new VisualState { Name = "Disabled" };

            // Create storyboards
            normalState.Storyboard = new Storyboard();
            mouseOverState.Storyboard = CreateMouseOverStoryboard();
            pressedState.Storyboard = CreatePressedStoryboard();
            disabledState.Storyboard = CreateDisabledStoryboard();

            // Add states to group
            commonStates.States.Add(normalState);
            commonStates.States.Add(mouseOverState);
            commonStates.States.Add(pressedState);
            commonStates.States.Add(disabledState);

            // Create states for FocusStates
            var focusedState = new VisualState { Name = "Focused" };
            var unfocusedState = new VisualState { Name = "Unfocused" };

            // Create storyboards
            focusedState.Storyboard = CreateFocusedStoryboard();
            unfocusedState.Storyboard = new Storyboard();

            // Add states to group
            focusStates.States.Add(focusedState);
            focusStates.States.Add(unfocusedState);

            // Add groups to collection
            VisualStateGroups.Add(commonStates);
            VisualStateGroups.Add(focusStates);
        }

        private Storyboard CreateMouseOverStoryboard()
        {
            var storyboard = new Storyboard();

            // Background color animation
            var colorAnimation = new ColorAnimationUsingKeyFrames();
            Storyboard.SetTargetName(colorAnimation, "Border");
            Storyboard.SetTargetProperty(colorAnimation, new PropertyPath("(Border.Background).(SolidColorBrush.Color)"));

            var keyFrame1 = new LinearColorKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0)),
                Value = Colors.LightBlue
            };

            colorAnimation.KeyFrames.Add(keyFrame1);
            storyboard.Children.Add(colorAnimation);

            // Opacity animation
            var doubleAnimation = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTargetName(doubleAnimation, "ContentPresenter");
            Storyboard.SetTargetProperty(doubleAnimation, new PropertyPath("Opacity"));

            var keyFrame2 = new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.2)),
                Value = 0.9
            };

            doubleAnimation.KeyFrames.Add(keyFrame2);
            storyboard.Children.Add(doubleAnimation);

            return storyboard;
        }

        private Storyboard CreatePressedStoryboard()
        {
            var storyboard = new Storyboard();

            var colorAnimation = new ColorAnimationUsingKeyFrames();
            Storyboard.SetTargetName(colorAnimation, "Border");
            Storyboard.SetTargetProperty(colorAnimation, new PropertyPath("(Border.Background).(SolidColorBrush.Color)"));

            var keyFrame = new DiscreteColorKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0)),
                Value = Colors.DarkBlue
            };

            colorAnimation.KeyFrames.Add(keyFrame);
            storyboard.Children.Add(colorAnimation);

            return storyboard;
        }

        private Storyboard CreateDisabledStoryboard()
        {
            var storyboard = new Storyboard();

            var opacityAnimation = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTargetName(opacityAnimation, "Border");
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath("Opacity"));

            var keyFrame = new SplineDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0)),
                Value = 0.5
            };

            opacityAnimation.KeyFrames.Add(keyFrame);
            storyboard.Children.Add(opacityAnimation);

            return storyboard;
        }

        private Storyboard CreateFocusedStoryboard()
        {
            var storyboard = new Storyboard();

            var thicknessAnimation = new ThicknessAnimationUsingKeyFrames();
            Storyboard.SetTargetName(thicknessAnimation, "Border");
            Storyboard.SetTargetProperty(thicknessAnimation, new PropertyPath("BorderThickness"));

            var keyFrame = new DiscreteThicknessKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0)),
                Value = new Thickness(2)
            };

            thicknessAnimation.KeyFrames.Add(keyFrame);
            storyboard.Children.Add(thicknessAnimation);

            return storyboard;
        }
    }
}


