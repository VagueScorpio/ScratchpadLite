Scratchpad Lite - the fonts folder
==================================

Drop font files (or .zip files of fonts) here. Subfolders are fine; they are
searched too. Anything you put in this folder shows up in the font picker
alongside the fonts Windows already has.

Scratchpad Lite never deletes, renames or overwrites what is in this folder.
It reads your files and keeps its own prepared copies in data\font_cache.


Font types that load directly
-----------------------------

  .ttf    TrueType
  .otf    OpenType
  .ttc    TrueType collection
  .otc    OpenType collection


Font types that are converted once, then cached
-----------------------------------------------

  .woff   Web font (WOFF 1.0)
  .woff2  Web font (WOFF 2.0)
  .pfb    Adobe Type 1 (binary)
  .pfa    Adobe Type 1 (ASCII)
  .dfont  Mac font container
  ._*     Mac AppleDouble file
  .eot    Embedded OpenType (uncompressed only)
  .t42    Type 42
  .zip    Any of the above, inside a zip

If a Type 1 font has a .pfm or .afm file beside it, keep them together: they
carry the metrics and the Windows font names.


Font types that cannot be used
------------------------------

  Bitmap fonts (.fon, .fnt, .bdf, .pcf), SVG fonts, Multiple Master and CID
  Type 1 fonts, compressed .eot files, and Mac fonts that lost their data when
  they were copied to Windows.

  Tools > Font Report lists every file in this folder and says what happened to
  it, including the reason for anything that could not be used.


The first run after adding a lot of fonts
-----------------------------------------

Preparing happens in the background, so you can start typing right away. New
fonts appear in the picker over the next moments. Later launches skip the work
entirely: a font that has not changed is never read again.

Added or removed fonts while the app is running? Tools > Reload Fonts picks up
new files. Removed fonts stay in the list until you restart, because Windows
cannot unload a font that is already in use.
