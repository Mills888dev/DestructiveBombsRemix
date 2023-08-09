//remix version reminder
using System.Collections.Generic;
using BepInEx;
using System.Linq;
using RWCustom;
using UnityEngine;
using System.Reflection;


namespace DestructiveBombs
{
    [BepInPlugin("DestructiveBombs", "Mills888", "0.2")]
    public partial class DestructiveBombs : BaseUnityPlugin
    {

        public static float configRadiusMul = 1f;

        private static FieldInfo _explosionReactorsNotified = typeof(Explosion).GetField("explosionReactorsNotified", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        [System.Obsolete]
        public void OnEnable()
        {
            On.Explosion.Update += Explosion_Update;
            On.RoomCamera.ApplyPositionChange += RoomCamera_ApplyPositionChange;
            On.Player.Update += Player_Update;
            On.RoomPreparer.Update += RoomPreparer_Update;
            On.Room.ShortCutsReady += Room_ShortCutsReady;
            On.RoomCamera.ChangeRoom += RoomCamera_ChangeRoom;
            On.AbstractRoom.Abstractize += AbstractRoom_Abstractize;

            AIRemapController.go = new GameObject();
            AIRemapController.go.AddComponent<AIRemapController>();
        }

        private void AbstractRoom_Abstractize(On.AbstractRoom.orig_Abstractize orig, AbstractRoom self)
        {
            AIRemapController.main.StopMappingRoom(self.realizedRoom);
            orig.Invoke(self);
        }

        private void RoomCamera_ChangeRoom(On.RoomCamera.orig_ChangeRoom orig, RoomCamera self, Room newRoom, int cameraPosition)
        {
            orig.Invoke(self, newRoom, cameraPosition);
            DestructionCache.NewRoom(newRoom.cameraPositions.Length);
        }

        private void Room_ShortCutsReady(On.Room.orig_ShortCutsReady orig, Room self)
        {
            List<AbstractWorldEntity> adList = new List<AbstractWorldEntity>();
            for (int i = self.abstractRoom.entities.Count - 1; i >= 0; i--)
            {
                if (self.abstractRoom.entities[i] is AbstractDestruction ad)
                {
                    adList.Add(ad);
                    self.abstractRoom.entities.RemoveAt(i);
                }
            }
            orig.Invoke(self);
            self.abstractRoom.entities.AddRange(adList);
        }

        [System.Obsolete]
        private void RoomPreparer_Update(On.RoomPreparer.orig_Update orig, RoomPreparer self)
        {
            bool lastDone = self.done;
            orig.Invoke(self);
            bool needsFinalization = false;
            if (self.done && !lastDone)
            {
                // Terrain destruction must be applied each time a room is realized
                foreach (AbstractWorldEntity entity in self.room.abstractRoom.entities)
                    if (entity is AbstractDestruction ad)
                    {
                        ad.ApplyTerrain(false);
                        if (ad.affectTerrain)
                            needsFinalization = true;
                    }
            }
            if (needsFinalization)
                AbstractDestruction.FinalizeTerrainDestruction(self.room);
        }

        private bool _spawnBomb = false;
        private void Player_Update(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig.Invoke(self, eu);
            if (self.room?.game?.devToolsActive ?? false)
            {
                if (Input.GetKey(KeyCode.T) && !_spawnBomb)
                {
                    AbstractPhysicalObject bomb = new AbstractPhysicalObject(self.room.world, AbstractPhysicalObject.AbstractObjectType.ScavengerBomb, null, self.coord, self.room.game.GetNewID());
                    bomb.pos = self.coord;
                    self.room.abstractRoom.AddEntity(bomb);
                    bomb.RealizeInRoom();
                    bomb.realizedObject.firstChunk.HardSetPosition(self.mainBodyChunk.pos + Vector2.up * 40f);
                }
                _spawnBomb = Input.GetKey(KeyCode.T);
            }
        }

        [System.Obsolete]
        private void RoomCamera_ApplyPositionChange(On.RoomCamera.orig_ApplyPositionChange orig, RoomCamera self)
        {
            // Cache the current screen, if needed
            // This is done on screen transition so each bomb won't individually cache the screen
            // Don't cache if the room is changing, though
            if (DestructionCache.IsScreenDirty(self.currentCameraPosition) && !self.AboutToSwitchRoom)
                DestructionCache.CacheTexture(self.currentCameraPosition, self.room.game.rainWorld.persistentData.cameraTextures[0, 0]);

            orig.Invoke(self);

            // Destruction must also be applied when the camera changes position
            bool mustApplyTexture = false;
            // Use a cached version of the texture, if available
            if (DestructionCache.HasTexture(self.currentCameraPosition) && !DestructionCache.IsScreenDirty(self.currentCameraPosition))
            {
                DestructionCache.ApplyTexture(self.currentCameraPosition, self.room.game.rainWorld.persistentData.cameraTextures[0, 0]);
            }
            else
            {
                foreach (AbstractWorldEntity entity in self.room.abstractRoom.entities)
                    if (entity is AbstractDestruction ad)
                    {
                        mustApplyTexture = true;
                        ad.ApplyVisual(false);
                    }
                if (mustApplyTexture)
                    self.room.game.rainWorld.persistentData.cameraTextures[0, 0].Apply();
            }
        }

        [System.Obsolete]
        private void Explosion_Update(On.Explosion.orig_Update orig, Explosion self, bool eu)
        {
            if (!(bool)_explosionReactorsNotified.GetValue(self))
            {
                if (self.damage > 0.5f)
                    DestructiveExplosion(self.room, self.pos, self.rad * 0.5f * configRadiusMul, (self.room.regionGate == null) && (self.room.shelterDoor == null));
            }
            orig.Invoke(self, eu);
        }

        [System.Obsolete]
        private void DestructiveExplosion(Room room, Vector2 pos, float rad, bool affectTerrain = true)
        {
            AbstractDestruction ad = new AbstractDestruction(room.world, room.GetWorldCoordinate(pos), pos, rad, room.game.GetNewID(), affectTerrain);
            room.abstractRoom.AddEntity(ad);
            ad.ApplyTerrain();
            ad.ApplyVisual();

            // All affected screens must have their caches cleared
            {
                Texture2D currentLevelTex = room.game.rainWorld.persistentData.cameraTextures[0, 0];
                for (int i = 0, len = room.cameraPositions.Length; i < len; i++)
                {
                    Vector2 testCamPos = room.cameraPositions[i];
                    IntVector2 testLocalPos = new IntVector2(Mathf.FloorToInt(ad.realPos.x - testCamPos.x), Mathf.FloorToInt(ad.realPos.y - testCamPos.y));
                    if ((testLocalPos.x < -rad) || (testLocalPos.y < -rad) || (testLocalPos.x > currentLevelTex.width + rad) || (testLocalPos.y > currentLevelTex.height + rad)) continue;
                    DestructionCache.ClearCachedTexture(i);
                    DestructionCache.SetScreenDirty(i);
                }
            }
        }
    }

    public class DestructionCache : MonoBehaviour
    {
        public static Color32[][] texCache = new Color32[0][];
        public static bool[] screenDirty = new bool[0];

        public static void SetScreenDirty(int cameraNumber)
        {
            if (cameraNumber < 0 || cameraNumber >= texCache.Length) return;
            screenDirty[cameraNumber] = true;
            //Debug.Log("Set " + cameraNumber + " dirty");
        }

        public static bool IsScreenDirty(int cameraNumber)
        {
            if (cameraNumber < 0 || cameraNumber >= texCache.Length) return false;
            return screenDirty[cameraNumber];
        }

        public static bool HasTexture(int cameraNumber)
        {
            if (cameraNumber < 0 || cameraNumber >= texCache.Length) return false;
            return texCache[cameraNumber] != null;
        }

        public static void CacheTexture(int cameraNumber, Texture2D tex)
        {
            if (cameraNumber < 0 || cameraNumber >= texCache.Length) return;
            texCache[cameraNumber] = tex.GetPixels32();
            screenDirty[cameraNumber] = false;
            //Debug.Log("Cached screen " + cameraNumber);
        }

        public static bool ApplyTexture(int cameraNumber, Texture2D dest)
        {
            if (cameraNumber < 0 || cameraNumber >= texCache.Length) return false;
            if (texCache[cameraNumber] == null) return false;
            dest.SetPixels32(texCache[cameraNumber]);
            dest.Apply();
            //Debug.Log("Applied screen " + cameraNumber);
            return true;
        }

        public static void ClearCachedTexture(int cameraNumber)
        {
            if (cameraNumber < 0 || cameraNumber >= texCache.Length) return;
            texCache[cameraNumber] = null;
            screenDirty[cameraNumber] = false;
            //Debug.Log("Cleared cache of " + cameraNumber);
        }

        public static void NewRoom(int newCount)
        {
            texCache = new Color32[newCount][];
            screenDirty = new bool[newCount];
            //Debug.Log("Cleared cache to new room");
        }

        public static void ClearCache()
        {
            texCache = new Color32[0][];
            screenDirty = new bool[0];
            //Debug.Log("Cleared cache to empty");
        }
    }

    public class AbstractDestruction : AbstractWorldEntity
    {
        public Vector2 realPos;
        public int rad;
        public bool affectTerrain;

        public AbstractDestruction(World world, WorldCoordinate pos, Vector2 realPos, float rad, EntityID ID, bool affectTerrain = true) : base(world, pos, ID)
        {
            this.realPos = realPos;
            this.rad = (int)rad;
            this.affectTerrain = affectTerrain;
        }

        [System.Obsolete]
        public void ApplyVisual(bool updateTexture = true)
        {
            Room rroom = Room.realizedRoom;
            if (rroom == null) return;

            int seed = Random.seed;
            Random.seed = ID.number;

            Vector2 camPos = rroom.cameraPositions[rroom.game.cameras[0].currentCameraPosition];
            IntVector2 localPos = new IntVector2(Mathf.FloorToInt(realPos.x - camPos.x), Mathf.FloorToInt(realPos.y - camPos.y));
            Texture2D levelTex = rroom.game.rainWorld.persistentData.cameraTextures[0, 0];

            // Only apply explosions that affect the current screen
            if ((localPos.x < -rad) || (localPos.y < -rad) || (localPos.x > levelTex.width + rad) || (localPos.y > levelTex.height + rad)) return;

            Vector2 normPos = Vector2.zero;
            int affectedPixels = 0;
            IntRect pixelRect = new IntRect(
                System.Math.Max(localPos.x - rad, 0),
                System.Math.Max(localPos.y - rad, 0),
                System.Math.Min(localPos.x + rad, levelTex.width),
                System.Math.Min(localPos.y + rad, levelTex.height)
            );

            Color[] pixelData = levelTex.GetPixels(pixelRect.left, pixelRect.bottom, pixelRect.Width, pixelRect.Height);
            for (int y = pixelRect.bottom; y < pixelRect.top; y++)
            {
                for (int x = pixelRect.left; x < pixelRect.right; x++)
                {
                    // Clip pixels to the radius (temporary)
                    normPos.Set((x - localPos.x) / (float)rad, (y - localPos.y) / (float)rad);
                    if (normPos.sqrMagnitude > 1f) continue;

                    // Modify the target pixel
                    //Color col = levelTex.GetPixel(x, y);
                    int pdIndex = (y - pixelRect.bottom) * pixelRect.Width + (x - pixelRect.left);
                    if ((pdIndex < 0) || (pdIndex >= pixelData.Length)) continue;
                    Color col = pixelData[pdIndex];

                    // Don't modify sky pixels, though
                    if (col.r == 1f && col.g == 1f && col.b == 1f) continue;
                    int r = Mathf.FloorToInt(col.r * 255f);
                    affectedPixels++;

                    bool lit = false;
                    if (r > 90)
                    {
                        r -= 90;
                        lit = true;
                    }
                    int paletteSlot = Mathf.FloorToInt(r / 30f);
                    int layer = (r - 1) % 30;
                    int oldLayer = layer;

                    // Move the world back some layers
                    // Add some normal and some radial noise
                    float ang = Vector2.Angle(Vector2.up, normPos);

                    const float explosionDepthMul = 0.75f;
                    const float moveDepthTowards = 40f;
                    const float noiseDepthMul = 10f;

                    float offset = Mathf.Clamp01(Mathf.PerlinNoise(x / 20f, y / 20f)) * noiseDepthMul * (1f - normPos.sqrMagnitude);
                    layer = System.Math.Max(Mathf.FloorToInt((1f - normPos.sqrMagnitude) * Mathf.Lerp(rad * explosionDepthMul, moveDepthTowards, 0.5f)) + Mathf.RoundToInt(offset), layer);
                    layer = Mathf.RoundToInt(Mathf.Lerp(layer, oldLayer, 0.5f));

                    // Make sure that there's a clear division between foreground and background
                    if (oldLayer < 6 && (layer - oldLayer < 6))
                    {
                        layer = oldLayer;
                    }

                    // Fit foreground destruction to terrain
                    if (PointInSolid(x + (int)camPos.x, y + (int)camPos.y) && (oldLayer < 10))
                    {
                        layer = oldLayer;
                    }

                    if (layer < 0) layer = 0;
                    if ((layer != oldLayer) && layer >= 29)
                    {
                        col.r = 255f;
                        col.g = 255f;
                        col.b = 255f;
                        //levelTex.SetPixel(x, y, col);
                        pixelData[pdIndex] = col;
                        continue;
                    }

                    // Remove lighting depending on the new layer
                    if (Mathf.Abs(layer - oldLayer) > 3f)
                    {
                        if (Vector2.Dot(normPos, -rroom.lightAngle) - normPos.magnitude < -1f)
                        {
                            lit = false;
                        }
                    }

                    col.r = (layer + 1 + paletteSlot * 30f + (lit ? 90 : 0)) / 255f;
                    //levelTex.SetPixel(x, y, col);
                    pixelData[pdIndex] = col;
                }
            }
            levelTex.SetPixels(pixelRect.left, pixelRect.bottom, pixelRect.Width, pixelRect.Height, pixelData);
            if (updateTexture)
                levelTex.Apply();

            Random.seed = seed;
        }

        [System.Obsolete]
        public void ApplyTerrain(bool finalize = true)
        {
            if (!affectTerrain) return;
            Room rroom = Room.realizedRoom;
            if (rroom == null) return;

            // No culling should happen

            int seed = Random.seed;
            Random.seed = ID.number;

            IntVector2 mins = rroom.GetTilePosition(realPos - new Vector2(rad, rad));
            IntVector2 maxs = rroom.GetTilePosition(realPos + new Vector2(rad, rad));

            // Erode terrain
            for (int tx = mins.x; tx <= maxs.x; tx++)
            {
                for (int ty = mins.y; ty <= maxs.y; ty++)
                {
                    Room.Tile tile = rroom.GetTile(tx, ty);
                    float dist = Vector2.Distance(rroom.MiddleOfTile(tile.X, tile.Y), realPos);
                    //bool hasNearbyShortcut = false;
                    //for(int ox = -1; ox <= 1; ox++)
                    //{
                    //    for(int oy = -1; oy <= 1; oy++)
                    //    {
                    //        if(rroom.GetTile(tx + ox, ty + oy).Terrain == global::Room.Tile.TerrainType.ShortcutEntrance)
                    //        {
                    //            hasNearbyShortcut = true;
                    //            break;
                    //        }
                    //    }
                    //}
                    //if (hasNearbyShortcut) continue;
                    if (dist < rad * 0.85f - Random.value * 30f)
                    {
                        if (tile.Terrain == global::Room.Tile.TerrainType.ShortcutEntrance)
                        {
                            shortcutsModified = true;
                            int ind = System.Array.IndexOf(rroom.shortcutsIndex, new IntVector2(tile.X, tile.Y));
                            rroom.shortcuts[ind].shortCutType = ShortcutData.Type.DeadEnd;
                            foreach (UpdatableAndDeletable obj in rroom.updateList)
                            {
                                if (obj is ShortcutHelper sh)
                                {
                                    for (int i = sh.pushers.Count - 1; i >= 0; i--)
                                    {
                                        ShortcutHelper.ShortcutPusher sp = sh.pushers[i];
                                        if (sp.shortCutPos.x == tile.X && sp.shortCutPos.y == tile.Y)
                                            sh.pushers.RemoveAt(i);
                                    }
                                }
                            }
                        }
                        if (dist < rad * 0.7f - Random.value * 15f)
                            tile.wallbehind = false;
                        tile.Terrain = global::Room.Tile.TerrainType.Air;
                        tile.horizontalBeam = false;
                        tile.verticalBeam = false;
                        if (tile.shortCut != 0)
                        {
                            shortcutsModified = true;
                            tile.shortCut = 0;
                        }
                    }
                }
            }

            // Convert all slopable blocks into slopes, with some random chance
            for (int tx = mins.x - 1; tx <= maxs.x + 1; tx++)
            {
                for (int ty = mins.y - 1; ty <= maxs.y + 1; ty++)
                {
                    Room.Tile tile = rroom.GetTile(tx, ty);
                    float dist = Vector2.Distance(rroom.MiddleOfTile(tile.X, tile.Y), realPos);
                    //bool hasNearbyShortcut = false;
                    //for(int i = 0; i < 4; i++)
                    //{
                    //    if(rroom.GetTile(tx + Custom.fourDirections[i].x, ty + Custom.fourDirections[i].y).Terrain == global::Room.Tile.TerrainType.ShortcutEntrance)
                    //    {
                    //        hasNearbyShortcut = true;
                    //        break;
                    //    }
                    //}
                    //if (hasNearbyShortcut) continue;
                    if (dist < rad * 0.85f + 20f)
                    {
                        if (tile.Terrain == global::Room.Tile.TerrainType.Solid)
                        {
                            if (IsSlopeValid(tx, ty) && (Random.value < 0.65f))
                            {
                                tile.Terrain = global::Room.Tile.TerrainType.Slope;
                            }
                        }
                    }
                }
            }

            // One last pass to remove any broken slopes
            for (int tx = mins.x - 1; tx <= maxs.x + 1; tx++)
            {
                for (int ty = mins.y - 1; ty <= maxs.y + 1; ty++)
                {
                    Room.Tile tile = rroom.GetTile(tx, ty);
                    if (tile.Terrain == global::Room.Tile.TerrainType.Slope && rroom.IdentifySlope(tx, ty) == global::Room.SlopeDirection.Broken)
                    {
                        tile.Terrain = global::Room.Tile.TerrainType.Air;
                    }
                }
            }

            Random.seed = seed;

            if (finalize) FinalizeTerrainDestruction(rroom);
        }

        public static bool shortcutsModified = false;

        public static void FinalizeTerrainDestruction(Room room)
        {
            if (shortcutsModified)
            {
                shortcutsModified = false;
                room.game.cameras[0].shortcutGraphics.NewRoom();
            }

            foreach (AbstractWorldEntity awe in room.abstractRoom.entities)
            {
                if (awe is AbstractSpear spear)
                {
                    if (spear.realizedObject is Spear rSpear && spear.stuckInWall && rSpear.stuckInWall.HasValue)
                    {
                        bool stillStuck = false;
                        for (int i = -1; i < 2; i += 2)
                        {
                            if ((spear.stuckInWallCycles >= 0 && room.GetTile(rSpear.stuckInWall.Value + new Vector2(20f * i, 0f)).Solid) || (spear.stuckInWallCycles < 0 && room.GetTile(rSpear.stuckInWall.Value + new Vector2(0f, 20f * i)).Solid))
                            {
                                stillStuck = true;
                                break;
                            }
                        }
                        if (!stillStuck)
                        {
                            spear.stuckInWallCycles = 0;
                            rSpear.ChangeMode(Weapon.Mode.Free);
                        }
                    }
                }
            }
            AIRemapController.main.StartMappingRoom(room);
            ValidatePlacedObjects(room);
        }

        public static void ValidatePlacedObjects(Room room)
        {
            List<UpdatableAndDeletable> entities = room.updateList;
            for (int i = entities.Count - 1; i >= 0; i--)
            {
                switch (entities[i])
                {
                    case DangleFruit df:
                        if (df.stalk != null)
                        {
                            if (!room.GetTile(df.stalk.stuckPos + Vector2.up * 10f).Solid)
                            {
                                if (!df.AbstrConsumable.isConsumed)
                                    df.AbstrConsumable.Consume();
                                df.stalk.fruit = null;
                                df.stalk.Destroy();
                                df.stalk = null;
                            }
                        }
                        break;
                    case SeedCob sc:
                        if (!room.GetTile(sc.rootPos - Vector2.up * 10f).Solid)
                        {
                            sc.Destroy();
                        }
                        break;
                }
            }
        }

        public bool IsSlopeValid(int tx, int ty)
        {
            Room rr = Room.realizedRoom;
            bool u = rr.GetTile(tx, ty + 1).Terrain == global::Room.Tile.TerrainType.Solid;
            bool r = rr.GetTile(tx + 1, ty).Terrain == global::Room.Tile.TerrainType.Solid;
            bool d = rr.GetTile(tx, ty - 1).Terrain == global::Room.Tile.TerrainType.Solid;
            bool l = rr.GetTile(tx - 1, ty).Terrain == global::Room.Tile.TerrainType.Solid;
            return (u && r && !d && !l) || (!u && r && d && !l) || (!u && !r && d && l) || (u && !r && !d && l);
        }

        public bool PointInSolid(int x, int y)
        {
            Room.Tile tile = Room.realizedRoom.GetTile(new Vector2(x, y));
            int lx = x - tile.X * 20;
            int ly = y - tile.Y * 20;
            switch (tile.Terrain)
            {
                case global::Room.Tile.TerrainType.Solid:
                    return true;
                case global::Room.Tile.TerrainType.Air:
                    return false;
                case global::Room.Tile.TerrainType.Floor:
                    return ly > 10;
                case global::Room.Tile.TerrainType.ShortcutEntrance:
                    return true;
                case global::Room.Tile.TerrainType.Slope:




                    if (Room.realizedRoom.IdentifySlope(tile.X, tile.Y) == global::Room.SlopeDirection.Broken)
                    {
                        return true;
                    }
                    else if (Room.realizedRoom.IdentifySlope(tile.X, tile.Y) == global::Room.SlopeDirection.DownRight)
                    {
                        return ly > lx;
                    }
                    else if (Room.realizedRoom.IdentifySlope(tile.X, tile.Y) == global::Room.SlopeDirection.UpLeft)
                    {
                        return ly < lx;
                    }
                    else if (Room.realizedRoom.IdentifySlope(tile.X, tile.Y) == global::Room.SlopeDirection.UpRight)
                    {
                        return ly < 20 - lx;
                    }
                    else if (Room.realizedRoom.IdentifySlope(tile.X, tile.Y) == global::Room.SlopeDirection.DownLeft)
                    {
                        return ly > 20 - lx;
                    }
                    /*switch (Room.realizedRoom.IdentifySlope(new Vector2Int(tile.X, tile.Y)))
                    {
                        case global::Room.SlopeDirection.Broken:
                            return true;
                        case global::Room.SlopeDirection.DownRight:
                            return ly > lx;
                        case global::Room.SlopeDirection.UpLeft:
                            return ly < lx;
                        case global::Room.SlopeDirection.UpRight:
                            return ly < 20 - lx;
                        case global::Room.SlopeDirection.DownLeft:
                            return ly > 20 - lx;
                    }*/
                    break;
            }
            return false;
        }
    }
}