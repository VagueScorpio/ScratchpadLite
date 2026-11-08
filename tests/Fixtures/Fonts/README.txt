Test fonts (Section 10.1). Test-only, never shipped, never committed.

The font tests need one freely licensed sample of each type the app claims to
handle, so Phase 2 and Phase 3 are tested against real files:

  .ttf .otf .ttc        the direct-load path
  .woff .woff2          the web font decoders
  .pfb + .pfm/.afm      the Type 1 converter, with metrics beside it
  .pfa                  the Type 1 ASCII form
  .dfont, ._sample      Mac containers, including an AppleDouble file
  .eot                  uncompressed, and one compressed to check the message
  .t42                  Type 42
  fonts.zip             several of the above inside a zip
  broken.ttf            a truncated file, to prove the bounds checks hold
  bitmap.fon            an unsupported type, to check the Font Report reason

Use fonts whose license allows redistribution for testing, and note each one's
source and license here when you add it.
