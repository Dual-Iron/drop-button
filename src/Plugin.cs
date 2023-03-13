using BepInEx;
using ImprovedInput;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RWCustom;
using System;
using System.Runtime.CompilerServices;
using System.Security.Permissions;
using UnityEngine;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace DropButton;

[BepInPlugin("com.dual.drop-button", "Drop Button", "1.0.1")]
sealed class Plugin : BaseUnityPlugin
{
    sealed class PlayerData { public PhysicalObject track; public int timer; }

    static readonly PlayerKeybind dropButton = PlayerKeybind.Register("dropbutton:dropbutton", "Drop Button", "Drop", KeyCode.C, KeyCode.JoystickButton3);
    static readonly ConditionalWeakTable<Player, PlayerData> tossed = new();

    static bool ActiveFor(Player p) => p.IsKeyBound(dropButton);

    public void OnEnable()
    {
        On.Player.ReleaseObject += Player_ReleaseObject;
        On.PlayerGraphics.Update += FixHand;
        IL.Player.GrabUpdate += Player_GrabUpdate;
    }

    private void Player_ReleaseObject(On.Player.orig_ReleaseObject orig, Player self, int grasp, bool eu)
    {
        bool toss = self.input[0].x != 0 && self.input[0].y >= 0 || self.input[0].x == 0 && self.input[0].y != 0;
        if (ActiveFor(self) && toss && self.grasps[grasp]?.grabbed is PhysicalObject grabbed) {
            LightToss(self, grasp, grabbed);
            return;
        }
        orig(self, grasp, eu);
    }

    private void FixHand(On.PlayerGraphics.orig_Update orig, PlayerGraphics self)
    {
        orig(self);

        PlayerData playerData = tossed.GetOrCreateValue(self.player);

        if (self.player.Consious && playerData.timer --> 0) {
            SlugcatHand hand = self.hands[self.handEngagedInThrowing];
            hand.reachingForObject = true;
            hand.absoluteHuntPos = playerData.track.firstChunk.pos;
            if (Custom.DistLess(self.head.pos, playerData.track.firstChunk.pos, 20f)) {
                hand.pos = playerData.track.firstChunk.pos;
            }
            else {
                hand.pos = self.head.pos + Custom.DirVec(self.head.pos, playerData.track.firstChunk.pos) * 20f;
            }
        }
        else {
            playerData.track = null;
        }
    }

    private static void LightToss(Player self, int grasp, PhysicalObject grabbed)
    {
        self.AerobicIncrease(0.5f);

        if (grabbed is Creature) {
            self.room.PlaySound(SoundID.Slugcat_Throw_Creature, self.grasps[grasp].grabbedChunk, false, 1f, 1f);
        }
        else {
            self.room.PlaySound(SoundID.Slugcat_Throw_Misc_Inanimate, self.grasps[grasp].grabbedChunk, false, 1f, 1f);
        }

        float angle = 45f;
        float speed = 4f;
        if (self.input[0].x != 0 && self.input[0].y == 0) {
            angle = Custom.LerpMap(grabbed.TotalMass, 0.2f, 0.3f, 60f, 50f);
            speed = Custom.LerpMap(grabbed.TotalMass, 0.2f, 0.3f, 12.5f, 8f, 2f);
        }
        else if (self.input[0].x != 0 && self.input[0].y == 1) {
            angle = 25f;
            speed = 9f;
        }
        else if (self.input[0].x == 0 && self.input[0].y == 1) {
            angle = 5f;
            speed = 8f;
        }
        if (self.Grabability(grabbed) == Player.ObjectGrabability.OneHand) {
            speed *= 2f;
            if (self.input[0].x != 0 && self.input[0].y == 0) {
                angle = 70f;
            }
        }
        if (self.input[0].x == 0 && self.input[0].y < 0) {
            angle = 180f;
            speed = 8f;
            for (int i = 0; i < grabbed.bodyChunks.Length; i++) {
                grabbed.bodyChunks[i].goThroughFloors = true;
            }
        }

        if (grabbed is PlayerCarryableItem pci) {
            speed *= pci.ThrowPowerFactor;

            pci.Forbid();
        }

        if (grabbed is Creature) {
            speed *= 0.2f;
        }

        if (grabbed.TotalMass < self.TotalMass * 2f && self.ThrowDirection != 0) {
            float throwPos = (self.ThrowDirection < 0) ? Mathf.Min(self.bodyChunks[0].pos.x, self.bodyChunks[1].pos.x) : Mathf.Max(self.bodyChunks[0].pos.x, self.bodyChunks[1].pos.x);
            foreach (BodyChunk chunk in grabbed.bodyChunks) {
                if (self.ThrowDirection < 0) {
                    if (chunk.pos.x > throwPos - 8f) {
                        chunk.pos.x = throwPos - 8f;
                    }
                    if (chunk.vel.x > 0f) {
                        chunk.vel.x = 0f;
                    }
                }
                else if (self.ThrowDirection > 0) {
                    if (chunk.pos.x < throwPos + 8f) {
                        chunk.pos.x = throwPos + 8f;
                    }
                    if (chunk.vel.x < 0f) {
                        chunk.vel.x = 0f;
                    }
                }
            }
        }

        if (!self.HeavyCarry(grabbed) && grabbed.TotalMass < self.TotalMass * 0.75f) {
            foreach (BodyChunk chunk in grabbed.bodyChunks) {
                chunk.pos.y = self.mainBodyChunk.pos.y;
            }
        }

        if (self.Grabability(grabbed) == Player.ObjectGrabability.Drag) {
            self.grasps[grasp].grabbedChunk.vel += Custom.DegToVec(angle * self.ThrowDirection) * speed / Mathf.Max(0.5f, self.grasps[grasp].grabbedChunk.mass);
        }
        else {
            foreach (BodyChunk chunk in grabbed.bodyChunks) {
                if (chunk.vel.y < 0f) {
                    chunk.vel.y = 0f;
                }
                chunk.vel = Vector2.Lerp(chunk.vel * 0.35f, self.mainBodyChunk.vel, Custom.LerpMap(grabbed.TotalMass, 0.2f, 0.5f, 0.6f, 0.3f));
                chunk.vel += Custom.DegToVec(angle * self.ThrowDirection) * Mathf.Clamp(speed / (Mathf.Lerp(grabbed.TotalMass, 0.4f, 0.2f) * grabbed.bodyChunks.Length), 4f, 14f);
            }
        }

        if (self.graphicsModule is PlayerGraphics g) {
            tossed.GetOrCreateValue(self).track = grabbed;
            tossed.GetOrCreateValue(self).timer = 3;
            g.handEngagedInThrowing = grasp;
        }

        self.dontGrabStuff = 10;
        self.room.socialEventRecognizer.CreaturePutItemOnGround(grabbed, self);
        self.ReleaseGrasp(grasp);
    }

    private void Player_GrabUpdate(ILContext il)
    {
        try {
            ILCursor cursor = new(il);

            // Navigate to the bool that decides whether or not to drop items
            cursor.Index = cursor.Body.Instructions.Count - 1;
            cursor.GotoPrev(MoveType.After, i => i.MatchLdloc(43));
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate(ShouldRelease);

            static bool ShouldRelease(bool orig, Player self)
            {
                return ActiveFor(self) ? self.JustPressed(dropButton) : orig;
            }

            // Ignore wantToPickUp for dropping items
            cursor.GotoPrev(MoveType.After, i => i.MatchLdfld<Player>("wantToPickUp"));
            cursor.Emit(OpCodes.Pop);
            cursor.Emit(OpCodes.Ldc_I4_1);

            // Use wantToPickUp for picking up items
            cursor.GotoNext(MoveType.After, i => i.MatchLdfld<Player>("pickUpCandidate"));
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate(PickUpCandidate);

            static PhysicalObject PickUpCandidate(PhysicalObject orig, Player self)
            {
                if (self.wantToPickUp <= 0) {
                    return null;
                }
                return orig;
            }
        }
        catch (Exception e) {
            Logger.LogError("Failed to hook GrabUpdate: " + e);
        }
    }
}
