using Avalonia.Headless.XUnit;
using OpenUtau.App.Studio;
using OpenUtau.App.ViewModels;
using ReactiveUI;
using Xunit;

namespace OpenUtau.App {
    [Collection(StudioUiSerialCollection.Name)]
    public class StudioTrackLayoutTest {
        [AvaloniaFact]
        public void StudioOn_UsesCompactHeightAndColorBar() {
            using var scope = new StudioUiScope(true);
            Assert.Equal(64, StudioTrackLayout.DefaultTrackHeight);
            Assert.Equal(64, StudioTrackLayout.CompactTrackHeight);
            Assert.Equal(6, StudioTrackLayout.ColorBarWidth);
        }

        [AvaloniaFact]
        public void StudioOff_UsesClassicHeightAndNoColorBar() {
            using var scope = new StudioUiScope(false);
            Assert.Equal(104, StudioTrackLayout.DefaultTrackHeight);
            Assert.Equal(0, StudioTrackLayout.ColorBarWidth);
        }

        [AvaloniaFact]
        public void CompactHeight_IsTwoWholeZoomStepsBelowClassic() {
            // 104 -> 84 -> 64: a single step still shows the phonemizer row.
            Assert.Equal(0, (104 - StudioTrackLayout.CompactTrackHeight) % 20);
            Assert.Equal(2, (104 - StudioTrackLayout.CompactTrackHeight) / 20);
        }

        [AvaloniaFact]
        public void TracksViewModel_StartsAtModeDefault() {
            using (new StudioUiScope(true)) {
                Assert.Equal(64, new TracksViewModel().TrackHeight);
            }
            using (new StudioUiScope(false)) {
                Assert.Equal(104, new TracksViewModel().TrackHeight);
            }
        }

        [AvaloniaFact]
        public void TracksViewModel_FollowsDefaultWhenUserHasNotZoomed() {
            using var scope = new StudioUiScope(false);
            var vm = new TracksViewModel();
            Assert.Equal(104, vm.TrackHeight);

            using (new StudioUiScope(true)) {
                MessageBus.Current.SendMessage(new StudioUIChangedEvent());
            }
            Assert.Equal(64, vm.TrackHeight);
        }

        [AvaloniaFact]
        public void TracksViewModel_KeepsUserZoomOnStudioToggle() {
            using var scope = new StudioUiScope(false);
            var vm = new TracksViewModel();
            vm.TrackHeight = 84;

            using (new StudioUiScope(true)) {
                MessageBus.Current.SendMessage(new StudioUIChangedEvent());
            }
            Assert.Equal(84, vm.TrackHeight);
        }
    }
}
