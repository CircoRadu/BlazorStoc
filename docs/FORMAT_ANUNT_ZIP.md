# Format ZIP pentru anunturi ("Incarca anunt")

Butonul "Incarca anunt" din Setari -> Anunturi primeste un `.zip` si il deschide in editor ca **draft nou** (niciodata publicat direct). Sablon: `docs/anunturi-sablon/`. Impachetare: `tools\make-anunt-zip.ps1 -Folder <director>`.

## Structura

```
anunt-<nume>.zip
  anunt.json        (obligatoriu)
  continut.html     (obligatoriu)
  imagini/          (optional: poza1.png, poza2.jpg ...)
```

Aceeasi structura o produce iconita "Descarca ca ZIP" de pe un rand al tabelului.

## anunt.json

| Camp | Tip | Sens |
|---|---|---|
| versiune | numar | trebuie sa fie 1 |
| titlu | text | obligatoriu, cel mult 200 de caractere |
| obligatoriu | true/false | obligatoriu = apare la fiecare logare pana la "Am citit" |
| audienta | "toti" sau "selectati" | "selectati" cere lista `utilizatori` |
| utilizatori | lista de nume de utilizator | utilizatorii inexistenti sau inactivi sunt omisi si raportati; daca nu ramane niciunul, audienta devine "toti" (avertisment) |
| validPana | "dd.mm.yyyy" sau gol | dupa aceasta data anuntul nu se mai arata |

## continut.html

Doar formatarile permise (restul se sterge la incarcare, ca la salvare): `h1-h4, p, div, span, br, hr, strong/b, em/i, u, s, ul, ol, li, blockquote, a, img`.
Atribute: `href` (doar https, http, mailto) la `a`; la `p, div, span, h1-h4, li` doar `style` cu `color:#rrggbb` / `rgb(...)` si `text-align:left|center|right|justify`; `src` si `alt` la `img`. Fara script, evenimente, iframe, formulare sau alte stiluri.
Pozele se scriu `<img src="imagini/poza1.png" alt="...">`; la incarcare sunt mutate in baza si adresa se inlocuieste.

## Limite

Arhiva: cel mult 40 MB si 60 de fisiere, 80 MB dezarhivata; o poza cel mult 5 MB (PNG, JPEG, GIF, WebP, verificate dupa continut); text cel mult 200.000 de caractere; fara cai cu `..`.

## Cum cer un anunt

Spune-i lui Claude ce vrei sa scrie anuntul (titlu, idee, tip, audienta, data). Claude scrie folderul in `artifacts/anunturi/<nume>/`, il impacheteaza cu `tools\make-anunt-zip.ps1` si iti da fisierul `.zip` de incarcat.
