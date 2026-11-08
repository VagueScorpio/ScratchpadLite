Test documents (Section 10.1). Not shipped, not committed.

Put sample files here that were made by other programs, so the format tests
read what those programs really write rather than what the blueprint says they
should:

  from-word.docx          Word: headings, lists, a table, an image, a hyperlink
  from-wordpad.rtf        WordPad: the plain case
  from-word.rtf           Word: images, tables, colors
  from-libreoffice.odt    LibreOffice Writer
  from-libreoffice.rtf    LibreOffice Writer
  windows-1252.txt        A txt file that is not UTF-8
  utf8-bom.txt            A txt file with a BOM
  sample.md               CommonMark plus pipe tables and task lists

Keep these small. They are read by the round-trip tests, so what matters is
that each one exercises a feature, not that it is long.
