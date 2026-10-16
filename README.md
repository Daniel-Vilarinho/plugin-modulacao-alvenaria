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

## Fluxo de trabalho: pavimento inteiro (recomendado)

1. **`ALVPLANTA`** na arquitetura limpa:
   - pergunta a unidade e a escala;
   - pede para **clicar uma linha de parede, uma de porta e uma de janela**, para aprender os layers
     daquele arquiteto (Enter reaproveita os da obra);
   - pede para **selecionar o pavimento** com uma janela de seleção.

   O plugin então detecta paredes, portas, janelas e encontros (L/T/X), **numera as paredes** e
   desenha os eixos para conferência. Depois pergunta se pode modular e, com Sim, desenha a
   **1ª fiada do pavimento todo**, com cotas.
2. Confira e edite a 1ª fiada à mão.
3. **`ALVPLANTA2`**: selecione a planta da 1ª fiada (arquitetura + blocos já editados) e indique um
   ponto nela e o correspondente na planta da 2ª fiada. O plugin desenha a **2ª fiada do pavimento
   todo**, lendo a 1ª como ela está, com numeração e cotas.
4. `ALVQUANT` e `ALVRELATORIO` para o quantitativo e as paredes que não fecharam.

**Regras dos encontros (amarração direta):**
- Em cada encontro passa direto **uma** parede, e ela se alterna entre as fiadas. Na 1ª fiada
  passa a parede que continua ou a maior (questionário, item 17); na 2ª, a perpendicular a ela.
- A parede que passa avança até a face externa da outra. A que não passa encosta nela, com 1 cm de
  junta.
- Trechos curtíssimos junto a portas (**bonecas**) que não fecham são apontados no relatório.

## Fluxo de trabalho: parede por parede

1. **`ALVOBRA`**: escolhe a família da obra (concreto 39 ou outra cadastrada), as peças
   que a construtora **não** usa e, se houver, os preços. Isso fica gravado **dentro do DWG**. Se você
   esquecer, o `ALV1FIADA` pergunta na primeira vez.
2. **`ALV1FIADA`**: pergunta a unidade (arquitetura em metros = `100`) e a escala. Depois pede a
   parede **por pontos**, com OSNAP: o **início** e o **fim** numa face, e **um ponto na face
   oposta**. O plugin calcula o eixo, então funciona com a parede em linha dupla, dentro do bloco
   da arquitetura e atravessando janelas. Se a parede for cortada por porta, pede o próximo segmento
   (Enter na face oposta reaproveita a espessura) e pergunta se cada vão é **porta, janela ou
   passagem**. Por fim, desenha a 1ª fiada e as cotas. As opções `Eixo` e `Faces` permitem clicar
   linhas em vez de pontos, inclusive dentro de blocos.
3. Confira e edite a 1ª fiada à mão, se precisar.
4. **`ALV2FIADA`**: pede o eixo, o que existe em cada ponta (parede, porta, janela ou passagem) e as
   peças da 1ª fiada **como estão desenhadas**. Depois pede um ponto na planta da 1ª fiada e o ponto correspondente na planta da 2ª, e
   desenha a 2ª fiada lá, com cotas. Na próxima parede, basta dar Enter para usar o mesmo deslocamento.
5. **`ALVQUANT`**: quantitativo de blocos em CSV, que abre no Excel, com total em R$ se houver preços.
6. **`ALVRELATORIO`**: relatório .docx com todas as paredes que não fecharam, para conferir e editar.

| Comando | O que faz |
|---|---|
| `ALVPLANTA` | Pavimento inteiro: detecta paredes/portas/janelas/encontros, numera e gera a 1ª fiada + cotas. |
| `ALVPLANTA2` | Pavimento inteiro: 2ª fiada a partir da 1ª desenhada, na planta da 2ª fiada. |
| `ALVOBRA` | Família, peças desabilitadas, preços, unidade e escala da obra (gravados no DWG). |
| `ALV1FIADA` | 1ª fiada + cotas (trechos e vãos). |
| `ALV2FIADA` | 2ª fiada a partir da 1ª desenhada, na planta da 2ª fiada, + cotas. |
| `ALVQUANT` | Quantitativo em CSV (Excel). Enter = desenho inteiro. |
| `ALVRELATORIO` | Relatório de inconsistências em Word (.docx). |
| `ALVCATALOGO` | Lista, adiciona, edita e remove peças e famílias. |
| `ALVVERSAO` | Versão instalada e caminho do catálogo. |
| `ALVATUALIZAR` | Procura e instala versão nova. |

### Regras de modulação

Valem para as duas fiadas, e todas as peças ficam sempre **1 cm uma da outra**:

| Peça | Onde pode entrar |
|---|---|
| **39** | Em qualquer lugar. **É sempre a primeira prioridade** (o mais barato). |
| **34** | Segunda prioridade. Recomendado **nos cantos**; no meio da parede só se for preciso adaptar. |
| **19** | **Só nas pontas, nunca no meio da parede.** É obrigatório na ponta que encosta em porta (a parede "acaba em 20": 19 + 1 de junta). |
| **54** | **Só no encontro em T**, na ponta da parede que atravessa a outra. Nunca em canto (L) nem no meio. |
| **9 e 4 (bolachas)** | **Nunca em canto, encontro ou ponta de porta** (precisa dos septos para o graute). Só no meio da parede, e por último. |

- Junta de 1 cm. Família 39: só fecham comprimentos terminados em **4 ou 9** (ex.: 599, 604, 639).
- **Porta:** o plugin **lê o tamanho da folha pelo arco** da porta no desenho e usa **tamanho + 11 cm**,
  centrado no vão. (A abertura que o arquiteto deixa na parede já inclui o marco: folha de 130 cm →
  abertura de 140 cm → alvenaria de 141 cm.) Sem arco, usa o vão desenhado + 11.
- **Janela:** tamanho do vidro + 6 cm. A janela não corta a 1ª nem a 2ª fiada, mas **aparece na cota**.
- **Cotas pela face da parede**, em cadeia: parede, porta (tamanho + 11), parede, janela (tamanho + 6)...
- 2ª fiada: defasagem mínima de **1/4 de bloco (10 cm)**, buscando **meio bloco (20 cm)**. A junta ao
  lado do 19 da porta é a mesma nas duas fiadas (exceção combinada). Se uma parede não tem solução com
  a defasagem mínima, ela é feita mesmo assim **com aviso no relatório** (nunca fica vazia).
- **Parede que não fecha:** as peças são colocadas encostadas na ponta que encosta em **outra parede**
  (canto/T), e a **sobra vai para o lado da porta**. Assim as peças de paredes diferentes ficam sempre a
  1 cm, e o relatório diz qual comprimento fecharia.
- **Blocos do seu desenho:** o plugin mede o bloco real (`P-Bloco39` etc.). Se ele não tiver o tamanho do
  catálogo (ex.: 40 em vez de 39, ou desenhado em metros), ajusta a escala na inserção e avisa.

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

### Comandos parede por parede

`ALV1FIADA`/`ALV2FIADA` seguem as mesmas regras. Como não há arco para ler, o `ALV1FIADA` **pergunta o
tamanho da porta (folha) e da janela (vidro)** de cada vão (Enter = o vão desenhado). O `ALV2FIADA` usa
exatamente o trecho que a 1ª fiada ocupa.

### Limitações atuais

- A leitura automática (`ALVPLANTA`) só pega paredes **ortogonais**. Paredes inclinadas são
  avisadas e devem ser feitas com `ALV1FIADA`/`ALV2FIADA`.
- A arquitetura não vem modulada, então vários trechos ficam com **sobra de poucos cm**. Eles vão
  para o relatório com os comprimentos que fecham, e o ajuste é manual.
- As regras de encontro seguem a amarração direta padrão e **precisam ser validadas pelo
  engenheiro** num pavimento real.
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

### Publicar uma versão (deploy)

```bash
git tag v0.3.0
git push origin v0.3.0
```

O workflow `Release` testa, compila, empacota e publica. O AutoCAD do engenheiro pergunta se quer
atualizar na próxima vez que abrir.
