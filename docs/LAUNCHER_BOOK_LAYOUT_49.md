# Book layout clarification (UI 49, after UI 48)

The book artwork, crop, mirrored rendering, placement and base 50% opacity are
unchanged. Only the protected content area changes: text may cover the dark
textured/ragged region, reserving the actual stack of page edges. Side reservation
uses 72/240 of the existing side slice plus 12 DIPs, rather than the full slice.
Top and bottom reservations also shrink to the actual border instead of excluding
the entire textured region.

The main page ScrollViewer keeps its original scrollbar control, commands and
style. A reused TranslateTransform places its vertical scrollbar on the right
paper edge. Only the outer page Scroller is affected; nested controls, dropdowns
and keybinding scrollbars retain their original positions. The scrolling content
presenter retains its own clip. PageFrame no longer clips the translated bar,
while MenuBookHost clips everything to the options column. Existing shortened
scrollbar height and audio-icon clearance are preserved.

Sidebar underlines use a fixed 8 DIP height for all captions, and constant-size
ornate end caps. Only a clean middle segment stretches horizontally with text
width. The opposite end uses a mirrored copy of the same cap. Start remains
excluded through UI 48's existing attached property. No new texture is extracted.

ArtOpacity binds to PageFrame.Opacity. Book brushes reuse two small mutable
ImageBrush instances and multiply the existing 0.5 art opacity by the page fade,
without adding a full-window group opacity surface. Navigation now runs the
existing exit fade before changing pages, followed by the existing entry effect;
the original cancellation token and navigation request guards are preserved.
The book does not slide; it follows the page opacity only.

Validation: exact atlas crops composed with the new geometry at 900x620,
1280x800 and 1920x1080; binding-only margins and symmetric fixed-height ornament
composition checked. Modified XAML parses. This environment has no .NET SDK or
WPF runtime, so Windows compilation, native hit testing of the relocated
scrollbar and live animation timing remain for the player's Windows test.

Apply after UI 48. Close the launcher, copy ARCHIVOS over the working clone,
then run bash tools/build-launcher-only.sh. No engine rebuild, config reset,
GUI asset deletion or re-extraction is needed.
