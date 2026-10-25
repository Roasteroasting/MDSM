using System;

namespace MDSM;

// A paged testing hotbar, with unlimited quantities and no inventory system.
public sealed class BlockPalette
{
    public const int SlotsPerPage = 9;
    public int SelectedIndex { get; private set; }
    public int Page => SelectedIndex / SlotsPerPage;
    public int SelectedSlot => SelectedIndex % SlotsPerPage;
    public int PageCount => (Blocks.Selection.Length + SlotsPerPage - 1) / SlotsPerPage;
    public BlockType Selected => Blocks.Selection[SelectedIndex];
    public BlockType AtSlot(int slot)
    {
        int index = Page * SlotsPerPage + slot;
        return slot >= 0 && slot < SlotsPerPage && index < Blocks.Selection.Length ? Blocks.Selection[index] : BlockType.Air;
    }
    public void SelectSlot(int slot)
    {
        if (AtSlot(slot) != BlockType.Air) SelectedIndex = Page * SlotsPerPage + slot;
    }
    public void NextPage() => SelectedIndex = (Page + 1) % PageCount * SlotsPerPage;
    public void Reset() => SelectedIndex = 0;
}
