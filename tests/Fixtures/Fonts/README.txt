Test fixture fonts
==================

Nothing in this folder is committed: it is in .gitignore, because font files are
large and most of these are not ours to redistribute. The font tests look here
and skip, rather than fail, when it is empty - so a clean clone and the CI
runner stay green (see tests/ScratchpadLite.Core.Tests/FontDecoderTests.cs).

Record every fixture here with where it came from and what it is for.


WOFF 2.0
--------

  GoNotoKurrent.woff2        from Emby Server's web UI. A very large CJK font:
                             64750 glyphs, long loca, and the font that caught
                             the bbox-bitmap padding bug, because it is full of
                             composite glyphs.
  intervariable.woff2        Inter, variable. 2926 glyphs, 1881 composites.
  mi_2024_05.woff2           Material Icons. 5461 glyphs, no composites at all,
                             which is why it decoded correctly even when the
                             composite path was broken.
  01dcbad1..., 0b075f0d...,  from a Brave extension. A spread of small fonts,
  1cc85c6a..., 473fc83d...   some with composites and some without.

Adobe Type 1
------------

  SY______.PFB + .PFM        Symbol, from Adobe Acrobat. A genuine symbol font:
                             its glyph names mean nothing to the Adobe Glyph
                             List, so it exercises the symbol-cmap path.
  ZX______.PFB + .pfm        Adobe Sans MM, from Acrobat. A real Multiple
  ZY______.PFB + .pfm        Master font, and Adobe Serif MM. Negative
                             fixtures: both must be refused with a reason, not
                             half-converted.
  NimbusRoman-Regular.pfa    URW's Type 1 fonts, from
  NimbusRoman-Bold.pfa       github.com/ArtifexSoftware/urw-base35-fonts.
  NimbusSans-Regular.pfa     Text fonts with Adobe Glyph List names and an AFM
  NimbusMonoPS-Regular.pfa   each, so they exercise the text-cmap path, the AFM
  + a matching .afm each     reader, and the PFA wrapper with a binary (not
                             hex) encrypted section.

EOT
---

  AdobeClean-Bold.eot        from Adobe Acrobat's Internet Explorer resources.
  AdobeClean-Light.eot       Uncompressed EOT, which is the recoverable kind.
  AdobeClean-Regular.eot
  photon-entypo.eot          from Typora. An icon font.

Not represented
---------------

  .dfont, .suit, ._* (Mac containers) and .t42 (Type 42). None were found on
  this machine, so the tests for those build their own fixtures. Drop real ones
  in here if you have any - the tests will pick them up.
