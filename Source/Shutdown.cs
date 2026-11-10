
namespace AlpineQuestPort {
    sealed partial class MapCanvas {
        internal void CancelBackgroundForClose(){ClearVisibilityScreen();Tiles.OnlineEnabled=false;Tiles.DiscardQueued();if(Elevation!=null){Elevation.Tiles.OnlineEnabled=false;Elevation.Tiles.DiscardQueued();}foreach(var layer in rasterLayers)layer.Dispose();rasterLayers.Clear();if(LiveRay!=null){LiveRay.Dispose();LiveRay=null;}}
    }
}
