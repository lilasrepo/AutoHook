using System;
using AutoHook.Enums;
using Dalamud.Game;

namespace AutoHook.SeFunctions;

public sealed class SeTugType : SeAddressBase
{
    public SeTugType(ISigScanner sigScanner)
        : base(sigScanner,
            "48 8D 35 ?? ?? ?? ?? 4C 8B CE")
    { }

    public unsafe BiteType Bite
        => Address != IntPtr.Zero ? *(BiteType*)Address : BiteType.Unknown;
}

