using ImprovedInput;
using UnityEngine;

namespace DropButton;

public static class Api
{
    public delegate bool OnDropDelegate(Player player, bool eu);

    public static readonly PlayerKeybind Drop = PlayerKeybind.Register("dropbutton:dropbutton", "Drop Button", "Drop", KeyCode.C, KeyCode.JoystickButton3);

    public static event OnDropDelegate OnDrop;

    public static bool JustPressedDrop(this Player player) => player.JustPressed(Drop);

    internal static void InvokeDrop(Player p, bool eu)
    {
        if (OnDrop == null) return;
        foreach (var method in OnDrop.GetInvocationList()) {
            var drop = (OnDropDelegate)method;
            if (drop(p, eu)) {
                return;
            }
        }
    }
}
