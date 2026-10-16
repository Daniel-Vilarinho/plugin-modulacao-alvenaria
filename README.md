# AlvMod — modulação de alvenaria autoportante para AutoCAD

Plugin para AutoCAD (2018 em diante; não roda no AutoCAD LT) que gera a 1ª e a 2ª fiada de
alvenaria autoportante, cota as paredes, gera o quantitativo de blocos (Excel) e o relatório de
inconsistências (Word).

- Análise do projeto e decisões do questionário: [docs/ANALISE.md](docs/ANALISE.md)

---

## Instalação (uma vez só)

1. Feche o AutoCAD.
2. Abra o **PowerShell** (menu Iniciar → digite "PowerShell") e cole:

   ```powershell
   irm https://raw.githubusercontent.com/Daniel-Vilarinho/plugin-modulacao-alvenaria/main/instalar.ps1 | iex
   ```

3. Abra o AutoCAD. A linha de comando mostra `AlvMod x.y.z carregado`.

O plugin fica em `%APPDATA%\Autodesk\ApplicationPlugins\AlvMod.bundle` e carrega sozinho (sem NETLOAD).

### Atualizações

Ao abrir o AutoCAD, se houver versão nova aparece **"Há uma nova versão do AlvMod… Atualizar agora?
[Sim] [Não]"**. Clique **Sim**, feche e abra o AutoCAD. Também dá para verificar com `ALVATUALIZAR`.

---

## Fluxo de trabalho

1. **`ALVOBRA`**: escolhe a família da obra (concreto 39 ou outra cadastrada), as peças
   que a construtora **não** usa e, se houver, os preços. Isso fica gravado **dentro do DWG**. Se você
   esquecer, o `ALV1FIADA` pergunta na primeira vez.
2. **`ALV1FIADA`**: pergunta a unidade e a escala, pede o eixo da parede (ou as duas faces, com a
   opção `Faces`) e os outros segmentos se a parede for cortada por porta. Para cada vão entre os
   segmentos, pergunta se é **porta, janela ou passagem**. Depois desenha a 1ª fiada e as cotas.
3. Confira e edite a 1ª fiada à mão, se precisar.
4. **`ALV2FIADA`**: pede o eixo, o que existe em cada ponta (parede, porta, janela ou passagem) e as
   peças da 1ª fiada **como estão desenhadas**. Depois pede um ponto na planta da 1ª fiada e o ponto correspondente na planta da 2ª, e
   desenha a 2ª fiada lá, com cotas. Na próxima parede, basta dar Enter para usar o mesmo deslocamento.
5. **`ALVQUANT`**: quantitativo de blocos em CSV, que abre no Excel, com total em R$ se houver preços.
6. **`ALVRELATORIO`**: relatório .docx com todas as paredes que não fecharam, para conferir e editar.

| Comando | O que faz |
|---|---|
| `ALVOBRA` | Família, peças desabilitadas, preços, unidade e escala da obra (gravados no DWG). |
| `ALV1FIADA` | 1ª fiada + cotas (trechos e vãos). |
| `ALV2FIADA` | 2ª fiada a partir da 1ª desenhada, na planta da 2ª fiada, + cotas. |
| `ALVQUANT` | Quantitativo em CSV (Excel). Enter = desenho inteiro. |
| `ALVRELATORIO` | Relatório de inconsistências em Word (.docx). |
| `ALVCATALOGO` | Lista, adiciona, edita e remove peças e famílias. |
| `ALVVERSAO` | Versão instalada e caminho do catálogo. |
| `ALVATUALIZAR` | Procura e instala versão nova. |

### Regras de modulação (do questionário)

- Junta de 1 cm. Família 39: só fecham comprimentos terminados em **4 ou 9** (ex.: 599, 604, 639).
- Prioridade: **máximo de blocos de 39**, depois **poucos compensadores**, e evitar paredes cheias de
  bloco de 34 (custos: 39 = 10, 19 = 12, 34 = 20, 9 = 22, 4 = 24; ajustáveis no `ALVCATALOGO`).
- **Nunca usa compensador encostado em porta/janela.**
- **Folga de marco:** o vão da alvenaria é o vão da arquitetura **+11 cm na porta** e **+6 cm na
  janela** (metade de cada lado). Passagem sem esquadria não soma folga. Os valores ficam na obra
  (`folgaPorta`/`folgaJanela`).
- **Cotas pela face da parede**, em cadeia: parede até o vão, o vão (já com a folga), do vão até o fim.
- 2ª fiada: defasagem mínima de **1/4 de bloco (10 cm)**, buscando **meio bloco (20 cm)**; junta a
  prumo nunca é desenhada.
- Parede que não fecha: as peças são colocadas a partir do início do trecho (ponta esquerda/inferior),
  a sobra fica no fim para ajuste manual e tudo entra no relatório.
- O bloco 54 (encontro de paredes) fica desligado até a fase de encontros L/T/X.

### Blocos e layers

Cada peça é inserida como **bloco** com nome, layer e cor do padrão do escritório:

| Bloco | Layer | Cor |
|---|---|---|
| `P-Bloco39` | `P-Bloco 14x39` | 74 |
| `P-Bloco34` | `P-Bloco 14x34` | 170 |
| `P-Bloco19` | `P-Bloco 14x19` | 204 |
| `P-Bloco54` | `P-Bloco 14x54` | 30 |
| `P-Bloco9` | `P-Bloco 14x9` | 8 |
| `P-Bloco4` | `P-Bloco 14x4` | 8 |

Entre um bloco e outro fica 1 cm de junta (argamassa). O plugin assume o ponto base no **canto inferior esquerdo** e o bloco desenhado em
**cm**. Se o bloco não existir no desenho, cria um bloco provisório (retângulo) com o mesmo nome; ao
inserir a biblioteca real, os provisórios são substituídos. Layers que já existem mantêm a cor do
escritório.

O catálogo fica em `%APPDATA%\AlvMod\catalogo.json` e pode ser editado pelo `ALVCATALOGO` ou à mão.
Catálogos criados pela v0.2 (com os nomes provisórios) são corrigidos sozinhos para esse padrão.

### Limitações atuais

- Paredes retas, uma por vez. Ainda não trata encontros L/T/X nem identifica portas e janelas sozinho
  (os segmentos selecionados indicam onde estão os vãos).
- Ainda não numera as paredes nem gera elevações.

---

## Para quem desenvolve

```
src/AlvMod.Core          motor, catálogo, quantitativo e relatório (netstandard2.0, SEM AutoCAD)
src/AlvMod.Plugin        comandos + atualizador (net48 → AutoCAD 2018–2024, net8.0-windows → 2025+)
tests/AlvMod.Core.Tests  testes (xUnit)
packaging/               PackageContents.xml do bundle
build/empacotar.ps1      compila e gera out/AlvMod-vX.Y.Z.zip
instalar.ps1             instalador para a máquina do engenheiro
```

As referências do AutoCAD vêm do NuGet oficial `AutoCAD.NET` (23.0 = AutoCAD 2019 para net48; 25.0 =
AutoCAD 2025 para net8) com `ExcludeAssets=runtime` ("Copiar Local = Falso").

```bash
dotnet test tests/AlvMod.Core.Tests
```

### Publicar uma versão (deploy)

```bash
git tag v0.3.0
git push origin v0.3.0
```

O workflow `Release` testa, compila, empacota e publica. O AutoCAD do engenheiro pergunta se quer
atualizar na próxima vez que abrir.
