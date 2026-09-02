using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewValley;
using StardewValley.Pathfinding;

namespace Yousv.JunimaticGreenhouseWorkers
{
    internal class GreenhouseJunimo : NPC
    {
        private float alpha = 1f;
        private float alphaChange;
        private readonly NetColor color = new NetColor();
        private bool destroy;

        private readonly Queue<Step> steps = new Queue<Step>();

        private const int WorkerSpeed = 3;
        private const int RespawnTimeoutMs = 6000;
        private const int DisgustEmote = 12;

        private Vector2 lastStuckPosition;
        private int stuckTime;
        private const float FadeSpeed = 0.05f;
        private const float SerializedScale = 0.6f;
        private const float SpawnedScale = 0.75f;
        private const float DrawLayerDivisor = 10000f;
        private const float ShadowYOffset = 44f;
        private const float ShadowLayerEpsilon = 1E-06f;
        private const float DrawOnTopLayer = 0.991f;
        private const float CarriedItemAlpha = 0.9f;
        private const int ForceUpdateLive = 99999;
        private const int AnimationFrameCount = 8;
        private const int RandomAnimateChance = 4;

        private static readonly Vector2[] BounceOffsetByFrame =
        {
            new Vector2(1, 12), new Vector2(3, 10), new Vector2(1, 8), new Vector2(-1, 6),
            new Vector2(-3, 4), new Vector2(-1, 4), new Vector2(1, 8), new Vector2(0, 10)
        };

        [XmlIgnore]
        public Item CarriedSeed;

        [XmlIgnore]
        public Item CarriedFertilizer;

        [XmlIgnore]
        public List<Item> CarriedHarvest;

        [XmlIgnore]
        public GreenhouseChestStorage HomeChest;

        [XmlIgnore]
        public Action OnDone;

        [XmlIgnore]
        public Vector2 PortalTile;

        [XmlIgnore]
        public Point PortalAccessTile;

        [XmlIgnore]
        public List<PlannedTask> DebugPlan;

        [XmlIgnore]
        public List<PlannedTask> DebugTasks;

        [XmlIgnore]
        public Point LastAccess;

        [XmlIgnore]
        public HashSet<Point> FailedAccessPoints = new HashSet<Point>();

        private int failedAccessTimer;
        private const int FailedAccessDecayMs = 30000;

        private bool onDoneFired;

        private string currentTask;
        private int disgustEmoteTimer;
        private bool returningToPortal;
        private const int DisgustEmoteDurationMs = 2200;

        private class Step
        {
            public Point Tile;
            public Action OnArrive;
        }

        public GreenhouseJunimo()
        {
            this.Initialize(SerializedScale);
            ModEntry.LogWarning("deserialization ctor invoked for a GreenhouseJunimo (should not happen during normal play).");
        }

        public GreenhouseJunimo(GameLocation currentLocation, Color color, AnimatedSprite sprite, Vector2 position, int facingDir, string name)
            : base(sprite, position, facingDir, name, null)
        {
            this.color.Value = color;
            this.currentLocation = currentLocation;
            this.Initialize(SpawnedScale);
        }

        private void Initialize(float scale)
        {
            this.Breather = false;
            this.speed = WorkerSpeed;
            this.forceUpdateTimer = ForceUpdateLive;
            this.ignoreMovementAnimation = true;
            this.farmerPassesThrough = true;
            this.Scale = scale;
            this.willDestroyObjectsUnderfoot = false;
            this.collidesWithOtherCharacters.Value = false;
            this.SimpleNonVillagerNPC = true;
            this.alpha = 0;
            this.alphaChange = FadeSpeed;
        }

        public void Enqueue(Point tile, Action onArrive)
        {
            this.steps.Enqueue(new Step { Tile = tile, OnArrive = onArrive });
        }

        public Point CurrentTile => new Point((int)(this.Position.X / 64f), (int)(this.Position.Y / 64f));

        [XmlIgnore]
        public bool IsDispatching;

        public bool IsIdle => !this.destroy && !this.IsDispatching && this.controller is null && this.steps.Count == 0;

        public bool IsDestroying => this.destroy;

        public void Start()
        {
            this.Advance();
        }

        public void FadeOut()
        {
            this.controller = null;
            this.destroy = true;
            this.currentTask = null;
            this.currentTaskKey = null;
            this.textAboveHeadTimer = 0;
            this.returningToPortal = false;
            this.disgustEmoteTimer = 0;
        }

        private string currentTaskKey;

        private void SetTaskInternal(string key, string display)
        {
            if (this.disgustEmoteTimer > 0)
                return;
            if (!ModEntry.Instance.Config.ShowTaskBubbles)
            {
                this.currentTask = null;
                this.currentTaskKey = null;
                this.textAboveHeadTimer = 0;
                return;
            }
            if (key == this.currentTaskKey)
                return;
            this.currentTaskKey = key;
            this.currentTask = display;
            if (key == "returning")
                this.returningToPortal = true;
            else if (key is not null)
                this.returningToPortal = false;
            this.showTextAboveHead(display);
        }

        public void SetHarvestingTask(string cropName) => this.SetTaskInternal("harvesting", $"Harvesting {cropName}");

        public void SetStoringTask(string itemName) => this.SetTaskInternal("storing", $"Storing {itemName}");

        public void SetPlantingTask(string seedName, string fertName = null) => this.SetTaskInternal("planting", fertName is not null ? $"Planting {seedName} with {fertName}" : $"Planting {seedName}");

        public void SetTakingSeedTask(string seedName) => this.SetTaskInternal("taking", $"Taking {seedName} from Chest");

        public void SetTakingSeedAndFertTask(string seedName, string fertName) => this.SetTaskInternal("taking", $"Taking {seedName} and {fertName} from Chest");

        public void SetTakingFertTask(string fertName) => this.SetTaskInternal("taking", $"Taking {fertName} from Chest");

        public void SetWateringTask() => this.SetTaskInternal("watering", "Watering crops");

        public void SetFertilizingTask(string plantName) => this.SetTaskInternal("fertilizing", $"Fertilizing {plantName}");

        public void SetReturningTask() => this.SetTaskInternal("returning", "Returning to portal");

        public void DoDisgustEmote()
        {
            this.doEmote(DisgustEmote);
            this.disgustEmoteTimer = DisgustEmoteDurationMs;
            this.currentTask = null;
            this.currentTaskKey = null;
            this.textAboveHeadTimer = 0;
        }

        public void SetReturningToPortal() => this.SetReturningTask();

        public void ClearReturningToPortal()
        {
            this.returningToPortal = false;
            if (this.currentTaskKey == "returning")
            {
                this.currentTask = null;
                this.currentTaskKey = null;
                this.textAboveHeadTimer = 0;
            }
        }

        private void DropCarriedAsDebris()
        {
            if (this.currentLocation is null)
                return;

            ModEntry.LogJunimo($"{this.Name} DropCarriedAsDebris: seed={this.CarriedSeed?.QualifiedItemId} x{this.CarriedSeed?.Stack ?? 0}, fert={this.CarriedFertilizer?.QualifiedItemId} x{this.CarriedFertilizer?.Stack ?? 0}, harvest={this.CarriedHarvest?.Count ?? 0}.");
            if (this.CarriedSeed is not null)
                this.currentLocation.debris.Add(new Debris(this.CarriedSeed, this.Position));
            if (this.CarriedFertilizer is not null)
                this.currentLocation.debris.Add(new Debris(this.CarriedFertilizer, this.Position));
            if (this.CarriedHarvest is { Count: > 0 })
            {
                foreach (var item in this.CarriedHarvest)
                    this.currentLocation.debris.Add(new Debris(item, this.Position));
            }

            this.CarriedSeed = null;
            this.CarriedHarvest = null;
            this.CarriedFertilizer = null;
        }

        private void Advance()
        {
            while (this.steps.Count > 0)
            {
                var step = this.steps.Dequeue();
                ModEntry.LogJunimo($"{this.Name} Advance: pathfinding to {step.Tile.X},{step.Tile.Y}.");
                bool found = this.TryGoTo(step.Tile, () =>
                {
                    try
                    {
                        step.OnArrive();
                    }
                    catch (Exception ex)
                    {
                        ModEntry.LogError($"{this.Name} Advance: OnArrive EXCEPTION at {step.Tile.X},{step.Tile.Y}: {ex}");
                        ModEntry.LogJunimo($"{this.Name} task failed with exception; dropping {this.CarriedSeed?.QualifiedItemId} x{this.CarriedSeed?.Stack ?? 0} seed, {this.CarriedFertilizer?.QualifiedItemId} x{this.CarriedFertilizer?.Stack ?? 0} fert, {this.CarriedHarvest?.Count ?? 0} harvest.");
                        this.DropCarriedAsDebris();
                        this.FadeOut();
                        return;
                    }

                    if (this.controller is null && !this.destroy)
                        this.Advance();
                });

                if (found)
                    return;

                ModEntry.LogWarning($"{this.Name} could not pathfind to {step.Tile.X},{step.Tile.Y}; failedAccessPoints += {step.Tile.X},{step.Tile.Y}, steps remaining={this.steps.Count}.");
                this.FailedAccessPoints.Add(step.Tile);
                this.steps.Clear();
                this.controller = null;
                this.stuckTime = 0;
                this.lastStuckPosition = this.Position;
                return;
            }
        }

        public bool TryGoTo(Point targetTile, Action onArrival)
        {
            this.controller = new PathFindController(this, this.currentLocation, targetTile, 0, (Character c, GameLocation loc) =>
            {
                this.controller = null;
                onArrival();
            });
            if (this.controller.pathToEndPoint != null)
                return true;
            this.controller = null;
            return false;
        }

        protected override void initNetFields()
        {
            base.initNetFields();
            base.NetFields
                .AddField(this.color, nameof(this.color));
        }

        public override void update(GameTime time, GameLocation farmHouse)
        {
            this.speed = Math.Clamp(ModEntry.Instance?.Config?.JunimoSpeed ?? WorkerSpeed, 1, 10);
            base.update(time, farmHouse);

            if (this.disgustEmoteTimer > 0)
            {
                this.disgustEmoteTimer -= (int)time.ElapsedGameTime.TotalMilliseconds;
                this.currentTask = null;
                this.currentTaskKey = null;
                this.textAboveHeadTimer = 0;
                if (this.disgustEmoteTimer <= 0)
                {
                    this.disgustEmoteTimer = 0;
                    if (this.returningToPortal && !this.destroy)
                        this.SetReturningTask();
                }
            }
            else if (this.currentTask is not null && this.textAboveHeadTimer <= 500)
                this.textAboveHeadTimer = 3000;

            if (this.destroy)
                this.alphaChange = -FadeSpeed;

            this.alpha += this.alphaChange;
            if (this.alpha > 1f)
                this.alpha = 1f;
            else if (this.alpha < 0f)
            {
                this.alpha = 0f;
                if (this.destroy)
                {
                    farmHouse.characters.Remove(this);
                    if (!this.onDoneFired)
                    {
                        this.onDoneFired = true;
                        ModEntry.LogJunimo($"{this.Name} fully removed from location; OnDone fired.");
                        this.OnDone?.Invoke();
                    }
                    return;
                }
            }

            if (Game1.IsMasterGame && !this.destroy)
            {
                if (this.FailedAccessPoints.Count > 0)
                {
                    this.failedAccessTimer += (int)time.ElapsedGameTime.TotalMilliseconds;
                    if (this.failedAccessTimer >= FailedAccessDecayMs)
                    {
                        this.FailedAccessPoints.Clear();
                        this.failedAccessTimer = 0;
                    }
                }
                else
                    this.failedAccessTimer = 0;

                if (this.steps.Count > 0)
                {
                    if (this.Position == this.lastStuckPosition)
                        this.stuckTime += (int)time.ElapsedGameTime.TotalMilliseconds;
                    else
                    {
                        this.stuckTime = 0;
                        this.lastStuckPosition = this.Position;
                    }

                    if (this.stuckTime > RespawnTimeoutMs)
                    {
                        this.RespawnAtPortal();
                        return;
                    }

                    if (this.controller is null)
                        this.Advance();
                }
                else
                {
                    this.stuckTime = 0;
                    this.lastStuckPosition = this.Position;
                }
            }

            this.UpdateAnimation(time);
        }

        private void RespawnAtPortal()
        {
            this.DropCarriedAsDebris();
            this.controller = null;
            this.steps.Clear();
            this.currentTask = null;
            this.currentTaskKey = null;
            this.textAboveHeadTimer = 0;
            this.disgustEmoteTimer = 0;
            this.returningToPortal = false;
            var tile = this.PortalAccessTile != Point.Zero
                ? this.PortalAccessTile
                : new Point((int)this.PortalTile.X, (int)this.PortalTile.Y);
            this.Position = new Vector2(tile.X * 64f, tile.Y * 64f);
            this.stuckTime = 0;
            this.lastStuckPosition = this.Position;
            ModEntry.LogJunimo($"{this.Name} was blocked; dropped carried items and returned to portal.");
        }

        private void UpdateAnimation(GameTime time)
        {
            this.Sprite.CurrentAnimation = null;
            int frame;
            bool isStationary = !(this.moveRight || this.moveLeft || this.moveUp || this.moveDown);
            if (this.moveRight || (isStationary && this.FacingDirection == 1))
            {
                frame = 16;
                this.flip = false;
            }
            else if (this.moveLeft || (isStationary && this.FacingDirection == 3))
            {
                frame = 16;
                this.flip = true;
            }
            else if (this.moveUp || (isStationary && this.FacingDirection == 0))
            {
                frame = 32;
            }
            else
            {
                frame = 0;
            }

            if (this.isMoving() || Game1.random.Next(RandomAnimateChance) == 0)
            {
                if (this.Sprite.Animate(time, frame, AnimationFrameCount, 50f))
                {
                    this.Sprite.currentFrame = frame;
                }
            }
        }

        public override void draw(SpriteBatch b, float alpha = 1f)
        {
            if (this.currentLocation is null || Game1.currentLocation is null)
                return;
            bool isAllowedLoc = ModEntry.Instance?.Config?.AllowAllLocations == true
                || (this.currentLocation?.Name == "Greenhouse" && Game1.currentLocation?.Name == "Greenhouse");
            if (!isAllowedLoc)
                return;
            if (this.alpha > 0f)
            {
                float num = (float)base.StandingPixel.Y / DrawLayerDivisor;
                b.Draw(
                    this.Sprite.Texture,
                    this.getLocalPosition(Game1.viewport)
                        + new Vector2(
                            this.Sprite.SpriteWidth * 4 / 2,
                            (float)this.Sprite.SpriteHeight * 3f / 4f * 4f / (float)Math.Pow(this.Sprite.SpriteHeight / 16, 2.0) + (float)this.yJumpOffset - 8f)
                        + ((this.shakeTimer > 0)
                            ? new Vector2(Game1.random.Next(-1, 2), Game1.random.Next(-1, 2))
                            : Vector2.Zero),
                    this.Sprite.SourceRect,
                    this.color.Value * this.alpha, this.rotation,
                    new Vector2(this.Sprite.SpriteWidth * 4 / 2,
                    (float)(this.Sprite.SpriteHeight * 4) * 3f / 4f) / 4f,
                    Math.Max(0.2f, this.Scale) * 4f, this.flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
                    Math.Max(0f, num) - ShadowLayerEpsilon);
                if (!this.swimming.Value)
                {
                    b.Draw(Game1.shadowTexture, Game1.GlobalToLocal(Game1.viewport, base.Position + new Vector2((float)(this.Sprite.SpriteWidth * 4) / 2f, 44f)), Game1.shadowTexture.Bounds, this.color.Value * this.alpha, 0f, new Vector2(Game1.shadowTexture.Bounds.Center.X, Game1.shadowTexture.Bounds.Center.Y), (4f + (float)this.yJumpOffset / 40f) * this.Scale, SpriteEffects.None, Math.Max(0f, num) - 1E-06f);
                }
            }

            if (!Game1.eventUp)
            {
                this.DrawEmote(b);
            }

            if (this.alpha > 0f)
            {
                float scaleFactor = (float)((Math.Cos(Game1.currentGameTime.TotalGameTime.Milliseconds * Math.PI / 512.0) + 1.0) * 0.05);
                var bounce = BounceOffsetByFrame[this.Sprite.CurrentFrame % BounceOffsetByFrame.Length];
                var baseOffset = new Vector2(8f, -64f * (float)this.Scale + 4f + (float)this.yJumpOffset) + bounce;
                if (this.CarriedSeed is not null && this.CarriedFertilizer is not null && (this.CarriedHarvest is null || this.CarriedHarvest.Count == 0))
                {
                    var seedPos = Game1.GlobalToLocal(Game1.viewport, base.Position + baseOffset);
                    float seedScaling = (float)this.Scale * (1 + scaleFactor);
                    var seedDrawStack = this.CarriedSeed.Stack > 1 ? StackDrawType.Draw : StackDrawType.Hide;
                    this.CarriedSeed.drawInMenu(b, seedPos, seedScaling, 1f, CarriedItemAlpha, seedDrawStack, Color.White, drawShadow: true);
                    var fertPos = Game1.GlobalToLocal(Game1.viewport, base.Position + baseOffset + new Vector2(18f, -14f));
                    float fertScaling = seedScaling * 0.62f;
                    var fertDrawStack = this.CarriedFertilizer.Stack > 1 ? StackDrawType.Draw : StackDrawType.Hide;
                    this.CarriedFertilizer.drawInMenu(b, fertPos, fertScaling, 1f, CarriedItemAlpha + 0.01f, fertDrawStack, Color.White, drawShadow: true);
                }
                else
                {
                    var carrying = new List<Item>();
                    if (this.CarriedSeed is not null)
                        carrying.Add(this.CarriedSeed);
                    if (this.CarriedFertilizer is not null)
                        carrying.Add(this.CarriedFertilizer);
                    if (this.CarriedHarvest is not null && this.CarriedHarvest.Count > 0)
                    {
                        var groups = new Dictionary<string, int>();
                        foreach (var item in this.CarriedHarvest)
                        {
                            groups.TryGetValue(item.QualifiedItemId, out int count);
                            groups[item.QualifiedItemId] = count + item.Stack;
                        }
                        foreach (var kvp in groups)
                            carrying.Add(ItemRegistry.Create(kvp.Key, kvp.Value));
                    }
                    float spacing = 50f;
                    float start = -spacing * (carrying.Count - 1) / 2f;
                    float xOffset = 0;
                    foreach (var carried in carrying)
                    {
                        var itemOffset = new Vector2(start + xOffset, 0);
                        xOffset += spacing;
                        var position = Game1.GlobalToLocal(Game1.viewport, base.Position + baseOffset + itemOffset);
                        float scaling = (float)this.Scale * (1 + scaleFactor);
                        var drawStack = carried.Stack > 1 ? StackDrawType.Draw : StackDrawType.Hide;
                        carried.drawInMenu(b, position, scaling, 1f, CarriedItemAlpha, drawStack, Color.White, drawShadow: true);
                    }
                }
            }
        }
    }
}
