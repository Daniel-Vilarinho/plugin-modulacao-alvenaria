# AlvMod — modulação de alvenaria autoportante para AutoCAD

Plugin para AutoCAD (2018 em diante; não roda no AutoCAD LT) que gera a 1ª e a 2ª fiada de
alvenaria autoportante do jeito que o engenheiro modula à mão — blocos, pontos de graute, nome das
paredes, aberturas, cotas internas e título —, além do quantitativo de blocos (Excel) e do relatório de
inconsistências com a memória de cálculo (Word).

- Requisitos do cliente, onde cada um está no código e qual teste prova: [docs/REQUISITOS.md](docs/REQUISITOS.md)
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

## Fluxo de trabalho: pavimento inteiro (recomendado)

1. **`ALVPLANTA`** na arquitetura limpa:
   - pergunta a **unidade** (quantos cm vale 1 unidade do desenho) e a escala. Arquitetura em 1:50 com
     1 unidade = 50 cm usa `50`; se a unidade estiver errada, o plugin percebe pela espessura das
     paredes (bloco 14 = parede de 14 a 20 cm) e **sugere a certa**;
   - pede para **clicar uma linha de parede, uma de porta e uma de janela**, para aprender os layers
     daquele arquiteto (Enter reaproveita os da obra);
   - pede para **selecionar o pavimento** com uma janela de seleção. Se a seleção já tem uma
     modulação anterior (blocos, graute, nomes, aberturas, cotas, eixos), pergunta se apaga antes de refazer;
   - detecta paredes, portas, janelas e encontros (L/T/X), põe os eixos na **grade de 5 cm** e desenha
     os eixos e números provisórios para conferência (layer `ALV-EIXOS`);
   - pergunta se há paredes que **não são de alvenaria** (drywall, shaft): clique em cada uma. Elas saem
     da modulação e ficam gravadas na obra;
   - pergunta se pode modular, o **quadro de janelas** ("medida x altura / peitoril" de cada largura
     desenhada) e se é para cotar;
   - desenha a **1ª fiada do pavimento todo**: blocos, pontos de graute nos encontros, "PAREDE n" (paredes
     iguais com o mesmo número), aberturas ("ABERTURA 91x220"), "PAREDE EM DRY WALL", cotas internas e
     o título "Planta de 1ª fiada - Modulação".
2. Confira e edite a 1ª fiada à mão.
3. **`ALVPLANTA2`**: selecione a planta da 1ª fiada (arquitetura + blocos já editados) e indique um
   ponto nela e o correspondente na planta da 2ª fiada. O plugin desenha a **2ª fiada do pavimento
   todo**, lendo a 1ª como ela está (quem passa em cada encontro e as juntas), com as mesmas anotações.
4. `ALVQUANT` e `ALVRELATORIO` para o quantitativo, as decisões (portas deslocadas, eixos ajustados) e a
   memória de cálculo.

**Como o plugin decide (o método do engenheiro):**
- Em cada encontro uma direção fica **por cima** na 1ª fiada e a outra na 2ª. O plugin escolhe, para o
  pavimento inteiro, a combinação que gasta **menos peças especiais**; no empate, nos T e cruzes, a
  parede vertical fica por cima na 1ª fiada.
- Cada **porta pode andar até 12 cm** ao longo da parede para os pedaços dos dois lados fecharem com
  blocos inteiros; do lado da dobradiça sempre fica alvenaria. O relatório diz quanto cada porta andou.
- **Apartamentos espelhados** recebem a modulação espelhada.

## Fluxo de trabalho: parede por parede

1. **`ALVOBRA`**: escolhe a família da obra (concreto 39 ou outra cadastrada), as peças que a
   construtora **não** usa, as folgas (porta = medida + 11, janela = medida + 1), quanto a porta pode
   andar (12 cm), a altura das portas (220), a numeração por tipo e, se houver, os preços. Isso fica
   gravado **dentro do DWG**. Se você esquecer, os comandos perguntam na primeira vez.
2. **`ALV1FIADA`**: pede a parede **por pontos**, com OSNAP: o **início** e o **fim** numa face, e
   **um ponto na face oposta**. O plugin calcula o eixo, então funciona com a parede em linha dupla,
   dentro do bloco da arquitetura e atravessando janelas. Se a parede for cortada por porta, pede o
   próximo segmento (Enter na face oposta reaproveita a espessura) e pergunta se cada vão é **porta,
   janela ou passagem** e a **medida nominal** (Enter = o vão desenhado arredondado). Depois pergunta o
   que a parede faz em cada ponta: **passa** no encontro, **encosta** na outra, termina **livre** ou numa
   **porta**. A janela não corta a 1ª fiada, mas entra na cota. As opções `Eixo` e `Faces` permitem
   clicar linhas em vez de pontos, inclusive dentro de blocos.
3. Confira e edite a 1ª fiada à mão, se precisar.
4. **`ALV2FIADA`**: pede o eixo, o que a parede faz em cada ponta **nesta 2ª fiada** e as peças da 1ª
   fiada **como estão desenhadas**. Depois pede um ponto na planta da 1ª fiada e o correspondente na da
   2ª, e desenha a 2ª fiada lá, com cotas. Na próxima parede, Enter usa o mesmo deslocamento.
5. **`ALVQUANT`**: quantitativo de blocos em CSV, que abre no Excel, com total em R$ se houver preços.
6. **`ALVRELATORIO`**: relatório .docx com as paredes que não fecharam, as decisões e a **memória de
   cálculo** (cada trecho: comprimento, pontas, peças em ordem e resultado).

| Comando | O que faz |
|---|---|
| `ALVPLANTA` | Pavimento inteiro: detecta, marca o que não é alvenaria e gera a 1ª fiada com graute, nomes, aberturas, cotas e título. |
| `ALVPLANTA2` | Pavimento inteiro: 2ª fiada a partir da 1ª desenhada, na planta da 2ª fiada. |
| `ALVOBRA` | Família, peças desabilitadas, folgas, porta, numeração, preços, unidade e escala da obra (gravados no DWG). |
| `ALV1FIADA` | 1ª fiada de uma parede + cotas. |
| `ALV2FIADA` | 2ª fiada de uma parede a partir da 1ª desenhada + cotas. |
| `ALVQUANT` | Quantitativo em CSV (Excel). Enter = desenho inteiro. |
| `ALVRELATORIO` | Relatório em Word (.docx): inconsistências, decisões e memória de cálculo. |
| `ALVCATALOGO` | Lista, adiciona, edita e remove peças e famílias. |
| `ALVVERSAO` | Versão instalada e caminho do catálogo. |
| `ALVATUALIZAR` | Procura e instala versão nova. |

### Regras de modulação

Valem para as duas fiadas, e todas as peças ficam sempre **1 cm uma da outra**:

| Peça | Onde pode entrar |
|---|---|
| **39** | Em qualquer lugar. **É sempre a primeira prioridade.** |
| **34** | **Juntos (até quatro) numa ponta do trecho**, nunca no meio: na ponta em que a parede passa no encontro; senão na cortada pela parede que chega no T; senão na que encosta. O plugin prefere vários 34 a usar bolachas. |
| **19** | **Só na ombreira de porta ou na ponta solta**, alternando 19 e 39 entre as fiadas. Num encontro, só como a peça única de uma boneca ao lado de porta. |
| **54** | **Peça fixa, centrada no encontro em T e em cruz**, na parede contínua, na fiada em que ela está por cima. Dois T muito próximos dividem um 54. |
| **9 e 4 (bolachas)** | **Só encostadas no encontro** (ao lado do 54 ou da parede que atravessa) ou como a boneca inteira. Nunca no quadrado do canto, nunca no meio da parede. |

**Amarração nos encontros:**

| Encontro | Fiada em que a parede A está por cima | Fiada em que a parede B está por cima |
|---|---|---|
| **Canto (L)** | A vai até a face externa e termina em **34**; B encosta a 1 cm | B vai até a face externa; A encosta a 1 cm |
| **T** (A contínua, B chega) | A recebe um **54 centrado** no T; B encosta nele | B atravessa A até a face de fora; A é cortada, com 1 cm de cada lado |
| **Cruz** | A recebe um **54 centrado**; B é cortada | B recebe um **54 centrado**; A é cortada |

- **Ponto de graute** no centro de todo encontro, nas duas fiadas.
- Junta de 1 cm. Família 39: só fecham comprimentos terminados em **4 ou 9** (ex.: 599, 604, 639). Por
  isso os eixos vão para a grade de 5 cm.
- **Porta:** abertura = medida nominal + 11 cm (porta de 80 → 91). A medida nominal é o vão desenhado,
  arredondado de 5 em 5 cm.
- **Janela:** abertura = medida + 1 cm (150 → 151). A janela não corta a 1ª nem a 2ª fiada, mas
  **aparece na cota** e na anotação. Se a medida real for outra, informe no quadro de janelas.
- **Cotas internas** por cômodo, de face a face dos blocos, com as aberturas no meio e o total por fora.
- 2ª fiada: defasagem mínima de **10 cm**, buscando **meio bloco (20 cm)**. Se uma parede não tem
  solução com a defasagem mínima, ela é feita mesmo assim **com aviso no relatório**.
- **Parede que não fecha:** as peças são colocadas a partir do encontro e a **sobra vai para o lado da
  porta**; o relatório diz qual comprimento fecharia.
- **Blocos do seu desenho:** o plugin mede o bloco real (em cm, em metro ou noutra escala) e insere na
  medida do catálogo. Só avisa quando a proporção do bloco não bate com a peça.

### Blocos e layers

Cada peça é inserida como **bloco** com nome, layer e cor do padrão do escritório:

| Peça | Bloco | Layer | Cor |
|---|---|---|---|
| 39 | `P-Bloco39` | `P-Bloco 14x39` | 74 |
| 34 | `P-Bloco 34` | `P-Bloco 14x34` | 170 |
| 19 | `P-Bloco 19` | `P-Bloco 14x19` | 204 |
| 54 | `P-Bloco 54` | `P-Bloco 14x54` | 30 |
| 9 | `Bloco 9x14` | `BOLACHA 14X4` | 8 |
| 4 | `bolacha` | `BOLACHA 14X4` | 8 |

As anotações vão nos layers `Ponto de GROUT - PLANTA` (bloco `PONTO DE GROUT PLANTA`), `TX PAREDES`
(nomes), `ABERTURA`, `FO-Cotas` e `FO-Títulos`, com texto Arial.

O nome do bloco é achado sem diferenciar espaço, hífen e maiúscula (`P-Bloco 34` = `P-BLOCO34`). Se o
bloco não existir no desenho, o plugin cria um provisório (retângulo) com o mesmo nome; ao inserir a
biblioteca real, os provisórios são substituídos. Layers que já existem mantêm a cor do escritório.

O catálogo fica em `%APPDATA%\AlvMod\catalogo.json` e pode ser editado pelo `ALVCATALOGO` ou à mão.
Catálogos de versões anteriores passam sozinhos para esse padrão, sem perder o que foi editado.

### Limitações atuais

- A leitura automática (`ALVPLANTA`) só pega paredes **ortogonais**. Paredes inclinadas são
  avisadas e devem ser feitas com `ALV1FIADA`/`ALV2FIADA`.
- O plugin só sabe o que está na arquitetura: porta que falta no desenho, janela com medida errada ou
  parede de drywall no layer de alvenaria precisam ser conferidas nos eixos antes de modular.
- Pontos de graute fora dos encontros (critério do engenheiro) ficam à mão.
- Ainda não gera as elevações (etapa 2).

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

Os testes com plantas reais de clientes leem `tests/AlvMod.Core.Tests/DadosLocais/`, que fica fora do
repositório; sem a pasta, eles passam sem fazer nada.

### Publicar uma versão (deploy)

```bash
git tag v0.3.0
git push origin v0.3.0
```

O workflow `Release` testa, compila, empacota e publica. O AutoCAD do engenheiro pergunta se quer
atualizar na próxima vez que abrir.
