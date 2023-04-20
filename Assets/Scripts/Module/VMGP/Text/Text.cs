using Nofun.VM;
using System;

namespace Nofun.Module.VMGP
{
    [Module]
    public partial class VMGP
    {
        private VMPtr<VMGPFont> activeFontPtr;
        private FontCache fontCache;
        private Font activeFont;

        [ModuleCall]
        private VMPtr<VMGPFont> vSetActiveFont(VMPtr<VMGPFont> newFont)
        {
            VMPtr<VMGPFont> previousFont = activeFontPtr;

            activeFontPtr = newFont;
            activeFont = fontCache.Retrieve(newFont.Read(system.Memory));

            return previousFont;
        }

        [ModuleCall]
        private void vPrint(Int32 transferMode, Int32 x, Int32 y, VMString value)
        {
            if (activeFont == null)
            {
                throw new InvalidOperationException("No font is currently active to print!");
            }

            activeFont.DrawText(system.GraphicDriver, system.Memory, x, y, value.Get(system.Memory),
                foregroundColor);
        }

        [ModuleCall]
        private uint vSelectFont(uint size, uint flags, ushort charShouldAvailable)
        {
            system.GraphicDriver.SelectSystemFont(size, flags, (int)charShouldAvailable);
            
            // Assume we use all
            return flags;
        }

        [ModuleCall]
        private void vTextOut(short x, short y, VMString text)
        {
            string textDraw = text.Get(system.Memory);
            system.GraphicDriver.DrawSystemText(x, y, textDraw, backgroundColor, foregroundColor);
        }

        [ModuleCall]
        private int vCharExtent(ushort charValue)
        {
            return system.GraphicDriver.GetStringExtentRelativeToSystemFont(char.ConvertFromUtf32(charValue));
        }
    }
}