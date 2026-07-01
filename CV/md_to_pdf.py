import markdown
from playwright.sync_api import sync_playwright

md_path = r'c:\MyData\Git\MyVaronis-BigResultHandler\CV\Ariel_Samin_CV_[5c].md'
pdf_path = r'c:\MyData\Git\MyVaronis-BigResultHandler\CV\Ariel_Samin_CV_[5c].pdf'

with open(md_path, 'r', encoding='utf-8') as f:
    md_content = f.read()

html_content = markdown.markdown(md_content)

html_full = """<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  body { font-family: Arial, sans-serif; font-size: 11pt; margin: 0; color: #111; line-height: 1.5; }
  h1 { font-size: 20pt; margin-bottom: 2px; }
  h2 { font-size: 13pt; border-bottom: 1px solid #aaa; padding-bottom: 3px; margin-top: 18px; }
  ul { margin: 4px 0 8px 0; padding-left: 20px; }
  li { margin-bottom: 4px; }
  p { margin: 4px 0; }
  a { color: #1a0dab; text-decoration: none; }
  strong { font-weight: bold; }
  hr { border: none; border-top: 1px solid #ddd; margin: 10px 0; }
</style>
</head>
<body>
""" + html_content + """
</body>
</html>"""

with sync_playwright() as p:
    browser = p.chromium.launch()
    page = browser.new_page()
    page.set_content(html_full, wait_until='load')
    page.pdf(path=pdf_path, format='A4', margin={'top': '2cm', 'bottom': '2cm', 'left': '2cm', 'right': '2cm'})
    browser.close()

print('PDF created:', pdf_path)
