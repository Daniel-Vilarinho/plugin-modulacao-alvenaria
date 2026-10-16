# Análise do resumo do projeto (resumo-plugin-modulacao-alvenaria.md)

Análise crítica do documento de contexto vindo da conversa anterior: falhas, lacunas,
viabilidade e dificuldade. Os itens marcados com **[decidir]** precisam de resposta do
engenheiro antes da fase correspondente.

---

## 0. Atualização: o que o questionário respondeu (v0.2)

| Item da análise | Resposta do questionário | O que mudou no código |
|---|---|---|
| 7 — pesos das peças | Máximo de 39; poucos compensadores, mas preferíveis a muitos 34 (q. 20, 21) | Custos 39 = 10, 19 = 12, 34 = 20, 9 = 22, 4 = 24. 604 cm = 15 × 39 + 4. |
| 8 — uso do 34 e do 54 | 54 é para encontros (q. 11) | 54 continua desligado até a fase de encontros. |
| 9 — sobreposição mínima | Mínimo 1/4 do bloco, ideal meio bloco (q. 18) | Mínimo 10 cm (proibido abaixo) e penalidade abaixo de 20 cm. |
| 6 — eixo × face | Trabalhar sempre no eixo (q. 24) | Mantido no eixo. Nova opção `Faces`: o eixo é calculado entre as duas faces (q. 38). |
| 12 — onde desenhar a 2ª fiada | Mesmo arquivo, sem separar fiadas por layer (q. 46) | A 2ª fiada vai para a planta da 2ª fiada (ponto na 1ª → ponto na 2ª), com os mesmos layers. |
| 13 — onde guardar o catálogo | Perguntar a família antes de modular, cadastrar blocos livremente (q. 52, 56) | Catálogo em JSON por computador (`ALVCATALOGO`); família e peças desabilitadas da obra gravadas no DWG (`ALVOBRA`). |
| 14 — cerâmico | 4, 19 e 39 (q. 9, 11) | Família `CERAMICO-39` cadastrada. |
| 18 — versões do AutoCAD | Todas (q. 59) | Duas DLLs: AutoCAD 2018–2024 (.NET 4.8) e 2025+ (.NET 8). LT não é possível. |
| 4 — repositório público | Cliente não precisa do código (q. 72) | Sem conflito. |
| — | Não modular quando não fecha, mas "pelo menos a parede chegando" (q. 22) | Coloca as peças a partir do início e deixa a sobra no fim, com aviso e relatório. |
| — | Nunca compensador ao lado de abertura (q. 34) | Proibido nas pontas de trecho que encostam em vão. |
| — | Cotas de parede e de vãos (q. 47) | Cotas alinhadas em cadeia (trecho, vão, trecho), dimensionadas pela escala. |
| — | Quantitativo no Excel, com preço quando houver (q. 7, 49, 54) | `ALVQUANT` gera CSV (abre no Excel) com total em R$. |
| — | Relatório de inconsistências em docx/pdf (q. 50) | `ALVRELATORIO` gera .docx. |
| — | Sempre pedir a escala (q. 37, 41) | Unidade e escala perguntadas em toda modulação (padrão = última usada na obra). |

### O que os DWG reais mostraram (arquitetura original + arquitetura limpa)

Arquivos: o projeto completo do arquiteto (implantação, cortes, plantas, 148 layers, sem
modulação) e a "arquitetura limpa" que o engenheiro extrai dele para modular (um pavimento tipo,
4 apartamentos espelhados). Os arquivos **não** foram colocados no repositório, que é público.

| Achado na arquitetura limpa | Consequência para o plugin |
|---|---|
| Tudo dentro de **um bloco** (`ARQUITETURA`) | Clicar numa linha selecionava o bloco inteiro. **Corrigido:** seleção dentro de blocos e modo por pontos. |
| Cabeçalho diz **mm**, desenho está em **metros** (39 × 30) | Confirma: sempre perguntar a unidade (aqui, `100`). |
| Paredes em **linha dupla** (faces) a **15 e 30 cm**, sem eixo; layer `Alvenaria` | Modo padrão agora é **por pontos** (início/fim numa face + face oposta), e o eixo é calculado. |
| Faces **quebradas** em cada encontro e janela | Selecionar objeto pega só um pedaço. Os pontos atravessam as quebras. |
| Janelas: parede interrompida no desenho, com linhas de **peitoril** (`Arq_Peitoril`) e **vidro** (`Arq_Esq_Vidro`) | Na 1ª e 2ª fiada a parede **continua** sob a janela: o usuário seleciona a parede inteira. |
| Portas: **linhas e arcos soltos** no layer `Portas` (não são blocos), rótulos P1/P2/P3 | A detecção automática (fase 4) vai ter que achar vãos pelas quebras das faces + layer `Portas`. |
| Paredes 100% **ortogonais** (0° e 90°) | Facilita a detecção automática de paredes e encontros. |
| Comprimentos das faces **não modulados** (a maioria termina em 0; muitos fracionados) | Parede por parede, quase tudo vai dar "não fecha". O comprimento real só sai com os **encontros** (fase 5), que viram a prioridade. |

### Correções do engenheiro após o segundo teste (v0.4.2)

| # | Erro apontado | Causa | Correção |
|---|---|---|---|
| 1, 7 | 19 no canto e no meio da parede | O 19 era aceito em qualquer ponta de trecho, inclusive onde a parede é cortada no encontro (para quem olha, é o meio da parede). | 19 **só em ponta sem continuidade** (porta ou parede solta). |
| 2, 5 | T e cruz sem 54; dois T vizinhos sem 54 | O 54 só entrava na ponta da parede que atravessa, e só se o comprimento favorecesse. | **54 fixo e centrado** em todo T e cruz, na parede que passa naquela fiada; T muito próximos (parede dupla) dividem um 54. Canto: 34 na que passa, 39 na que encosta. |
| 3 | Junta de 1,5 cm | O desenho aceitava até 0,5 cm de diferença no comprimento e jogava essa diferença numa ponta. | A diferença vai para o lado da porta ou é dividida entre todas as juntas. |
| 4, 6 | Blocos desalinhados | Combinação dos itens acima (19 e bolachas junto aos encontros, sobra no encontro). | Padrão de encontro com meio bloco de defasagem; bolachas e 34 de ajuste empurrados para o meio da parede. |
| — | "Method not found" depois de atualizar | O AutoCAD misturava a parte nova com a antiga quando se usava o comando sem reiniciar. | O plugin carrega as duas partes ao abrir, então nunca mistura versões. |

**Padrão de encontro adotado (confirmar com o engenheiro):** ver a tabela "Amarração nos encontros" no
README. Ele dá exatamente 20 cm de defasagem entre as fiadas nos cantos, T e cruzes.

### Correções do engenheiro após o primeiro teste (v0.4.1)

| # | Correção pedida | Como ficou |
|---|---|---|
| 1 | O 54 só no encontro em T | O 54 só entra na ponta da parede que atravessa um T (na fiada em que ela passa direto). Nunca em canto, X, nem no meio. |
| 2 | Parede que acaba na porta acaba em 20 | **Interpretação:** a última peça junto à porta é o meio bloco (19, "20" com a junta). Vale nas duas fiadas; a junta ao lado desse 19 é igual nas duas (exceção ao "sem prumo"). Se não couber, relaxa com aviso. |
| 3 | 54 e 19 não no meio da parede | 19 e 54 só nas pontas. |
| 4 | Espaço de 5,5 cm entre blocos | Dentro da parede todas as peças já ficavam a 1 cm. O 5,5 vinha da **sobra** de trechos que não fecham, que ficava junto ao encontro (até 4,5 + 1 de junta). Agora a sobra vai para o lado da porta e as peças encostam sempre na outra parede. Além disso o plugin mede o bloco real do desenho e corrige a escala se ele não tiver o tamanho do catálogo. |
| 5 | Priorizar o 39 | Custos 39 = 10; 34 = 40; 19 = 60; 9 = 80; 4 = 90. |
| 6 | 34 em cantos; no meio só se preciso | 34 custa menos em canto (25) do que no meio (40), e é sempre a 2ª prioridade depois do 39. |
| 7 | Bolacha nunca no canto | Bolacha (4 e 9) fora de ponta de porta, encontro e canto. |
| 8 | Ler o tamanho das portas: vão + 11; janela: vão + 6 | Porta: tamanho da folha lido pelo **arco** (folha 130 → 141 na alvenaria, e não 151 como antes). Janela: tamanho do vidro + 6, centrada, e entra na cota. |

**Medido na planta real:** as portas têm folha de 130 e 150 cm, com abertura de 140 e 160 cm na parede
(a abertura já inclui o marco, folha + 10). Antes o plugin somava +11 em cima da abertura, deixando a
alvenaria cerca de 10 cm mais larga do que o correto. O vidro das janelas tem exatamente o tamanho da
abertura.

**Pontos para o engenheiro confirmar:**
1. Item 2: "acabar em 20" é o meio bloco (19) junto à porta, nas duas fiadas?
2. Quando uma parede não fecha, vale ajustar a **posição da porta** em alguns centímetros (para a
   alvenaria ficar modular)? Hoje o plugin só avisa e deixa a sobra do lado da porta.
3. O 54 do T está na parede que atravessa (a "perpendicular"), certo?

### Respondido pelo engenheiro depois do questionário (v0.3)

| Pergunta | Resposta | No código |
|---|---|---|
| Nomes dos blocos | `P-Bloco39`, `P-Bloco34`, `P-Bloco19`, `P-Bloco54`, `P-Bloco4`, `P-Bloco9` (P = planta) | Catálogo padrão com esses nomes; layers `P-Bloco 14x39`… com as cores da legenda (74, 170, 204, 30, 8, 8). |
| Blocos cerâmicos | Não existem, só concreto | Família cerâmica removida do padrão (dá para cadastrar pelo `ALVCATALOGO` se um dia precisar). |
| Ponto base | Canto inferior esquerdo | Mantido. |
| Folga de marco | Porta = vão + 11; janela = vão + 6, entra no tamanho da parede | Cada parede ao lado do vão encolhe metade da folga; o comando pergunta porta/janela/passagem. |
| Cotas | Pelas faces (as paredes vão ser encaixadas) | Cotas na face. Em parede isolada a medida é igual à do eixo; nos encontros L/T/X a diferença aparece. |

### Ainda em aberto

1. **NETLOAD (q. 64) × carregamento automático:** a atualização dentro do plugin (q. 73) exige o
   carregamento automático, então ele foi mantido. Tudo bem?
2. **Arquivos reais (q. 43):** a arquitetura **sem** modulação + o **mesmo pavimento modulado à mão**
   (1ª e 2ª fiada) + as elevações. São necessários para os encontros de parede e o pavimento inteiro.
3. **Canaletas (verga/contraverga):** nomes dos blocos para a fase das elevações.

---

## 1. Veredito rápido

| Aspecto | Avaliação |
|---|---|
| Viabilidade técnica | **Alta.** Tudo o que foi pedido é possível com a API .NET do AutoCAD. Nada exige ObjectARX/C++. |
| Dificuldade do MVP (1ª e 2ª fiada, parede reta) | **Média-baixa.** O motor é um problema pequeno de programação dinâmica. |
| Dificuldade da versão completa | **Alta.** A parte difícil não é o algoritmo, é **ler desenhos sem padrão** (aberturas, encontros, unidades) e as **regras com exceção**. |
| Maior risco | Regras de engenharia não escritas ("cada caso é um caso") e desenhos de arquitetos diferentes. |
| Gargalo | Ciclo testar no AutoCAD → relatar → corrigir. **Resolvido em parte** pelo auto-update deste repositório. |

---

## 2. Falhas e inconsistências encontradas no resumo

### 2.1 Erros objetivos

1. **Teste com 640 cm não vai fechar.** O próximo passo 1 manda testar com 599, 604 **ou 640 cm**,
   mas o próprio documento diz que só fecham comprimentos terminados em 4 ou 9. 640 vai dar
   `[INCONSISTÊNCIA]` de novo. Valores válidos próximos: 639 e 644.

2. **"Programação dinâmica em unidades de meio-módulo" está errado.** Meio-módulo = 20 cm, mas as
   peças + junta variam de 5 em 5 cm (40, 35, 20, 55, 10, 5). O passo correto da grade é **5 cm**
   (o MDC de todas as peças + junta). Com passo de 20 cm, o 34, o 54, o 9 e o 4 nem caberiam.
   Neste repositório o motor calcula o passo sozinho a partir do catálogo.

3. **Nomes de blocos DWG divergentes.** A seção 5 (arquivos reais) lista `Bloco_39x14`,
   `BOLACHA 14X4`, `Bloco_54x14`. A seção 6 (catálogo) usa `P-Bloco 34`, `Bloco 9x14`,
   `P-Bolacha 4x14`. Um dos dois está errado, ou cada projeto usa nomes diferentes.
   Como a identidade da peça é **nome do bloco + layer**, isso precisa ser **configurável por
   obra**, e não fixo no código.

4. **Conflito entre "entrega só do binário, sem código-fonte" e o repositório público.**
   O repositório agora é público, então qualquer pessoa pode ver o código. Se existir um cliente
   (construtora) que exigiu "só DLL", o modelo precisa mudar (repositório privado + releases
   públicas). **[decidir]**

5. **"NETLOAD manual" e "SECURELOAD = 0" estão desatualizados e são inseguros.** Desligar o
   SECURELOAD libera qualquer DLL em qualquer pasta. Este repositório instala o plugin como
   *bundle* em `%APPDATA%\Autodesk\ApplicationPlugins`, que já é pasta confiável do AutoCAD:
   ele carrega sozinho, sem NETLOAD e sem mexer no SECURELOAD.

### 2.2 Lacunas (não estão erradas, mas faltam definir)

6. **Eixo × face da parede.** As paredes estão como polilinha **no eixo**. Em parede isolada tanto
   faz, mas num encontro em L/T o comprimento útil de cada parede muda em meia largura (7 cm) de cada
   lado. Precisa ficar decidido **qual comprimento entra no motor**: eixo, face interna ou face
   externa. **[decidir]**

7. **A função objetivo não tem pesos.** "Maximizar blocos de 39 e minimizar amarração e
   compensador" não diz o que fazer num empate. Exemplo real com **604 cm**:
   - 13 × 39 + 1 × 54 + 1 × 19 + 1 × 9? Ou
   - 15 × 39 + 1 bolacha de 4? (bolacha **não é grauteável**) Ou
   - 14 × 39 + 1 × 34 + 1 × 9? Ou
   - 14 × 39 + 2 × 19 + 1 × 4?

   Todas fecham. Qual é a certa? Hoje o motor usa uma tabela de **custos por peça** (em
   `CatalogoPadrao.cs`) que o engenheiro precisa calibrar. **[decidir]**

8. **Onde entram o 34 e o 54.** O resumo diz "34 = amarração" e "54 = encontro", mas não dá a
   regra. Em parede isolada o 54 deveria ser proibido? O 34 só aparece nos cantos? **[decidir]**

9. **Sobreposição mínima entre fiadas.** "Sem junta a prumo" não diz quanto deslocamento basta.
   Com essa família, o deslocamento mínimo possível é 5 cm. 5 cm conta como amarração aceitável, ou
   o mínimo é 9, 10 ou 20 cm? O motor tem esse parâmetro (`SobreposicaoMinima`, padrão 5 cm).
   **[decidir]**

10. **"Defasagem" da 2ª fiada.** O protótipo pergunta a defasagem, mas a 2ª fiada já é calculada a
    partir das juntas da 1ª. As duas coisas competem. Aqui a pergunta foi removida: o motor escolhe a
    melhor 2ª fiada que respeita a sobreposição mínima.

11. **Ponto base dos blocos DWG.** Para inserir `P-Bloco39` no lugar certo, o plugin precisa saber
    onde fica o ponto de inserção do bloco (canto? centro? face?) e se o bloco foi desenhado em cm.
    Isso não aparece no resumo. Hoje o plugin assume **canto inferior esquerdo, desenhado em cm, ao
    longo do eixo X**. Se o bloco não existir no desenho, ele desenha um retângulo. **[decidir com um DWG real]**

12. **Onde desenhar a 2ª fiada.** No mesmo lugar da 1ª (em outro layer) ou deslocada ao lado, como
    em prancha? Hoje ela é desenhada no mesmo lugar, em layers com sufixo ` 2F`. **[decidir]**

13. **Onde o catálogo e as "peças habilitadas por obra" ficam guardados.** LiteDB num arquivo em
    `%APPDATA%` fica **por computador**: as 3 máquinas teriam catálogos diferentes. Sugestão:
    - catálogo de famílias: arquivo JSON/LiteDB versionado e distribuído **junto com o plugin** (vai
      com o auto-update);
    - peças habilitadas da obra: gravadas **dentro do próprio DWG** (NOD/XRecord). Assim, quem abrir o
      arquivo usa a mesma configuração.

14. **Bloco cerâmico.** O material "cerâmico" aparece como opção, mas só a família 39 de concreto foi
    detalhada. Precisa da lista de peças cerâmicas (comprimentos, junta, nomes de bloco e layers).

15. **Validações V-01 a V-10.** O resumo só cita 3 delas (junta a prumo, compensador empilhado e
    bolacha em prumada de graute). As outras 7 estão no PRD, que está desatualizado.

16. **Numeração das paredes.** A regra "X de cima para baixo, depois Y da esquerda para a direita" não
    diz o que fazer com **paredes inclinadas**, nem qual é a tolerância para considerar uma parede
    "em X".

17. **Porta = quebra da polilinha.** Funciona nos arquivos vistos, mas a polilinha também pode quebrar
    por outros motivos (shaft, janela até o piso, erro de desenho). Por isso a decisão de **detecção
    assistida com confirmação** está correta. Basta não confiar só no gap.

18. **"Todas as versões do AutoCAD".** Por decisão atual, o foco é **AutoCAD 2022–2024 (.NET
    Framework 4.8)**. O 2025+ usa .NET 8 e exige uma segunda DLL. O AutoCAD LT **não roda** plugin
    .NET em nenhuma versão: confirmar que ninguém da equipe usa LT.

### 2.3 Pontos fortes (manter)

- Motor sem referência ao AutoCAD, testável isoladamente.
- **Não forçar** quando não fecha: avisa e devolve o controle ao usuário.
- Fluxo em duas etapas (1ª fiada → edição manual → 2ª fiada lida do desenho).
- Sempre perguntar a unidade, porque o cabeçalho do DWG mente.
- Detecção de aberturas **assistida**.

---

## 3. Viabilidade e dificuldade por fase

| Fase | Entrega | Dificuldade | Comentário |
|---|---|---|---|
| 0 | Ambiente + Hello AutoCAD | Baixa | **Feito aqui**, com build no GitHub e auto-update. |
| 1 | Catálogo + seleção de família e peças habilitadas | Baixa | Depende da decisão do item 13. |
| 2 | Motor de parede isolada | Baixa-média | **Feito aqui** (com testes). Falta calibrar os pesos (item 7). |
| 3 | Integração 1ª/2ª fiada | Média | **Versão inicial feita.** Falta validar com DWG real (itens 11 e 12). |
| 4 | Leitura de geometria + aberturas | **Alta** | Desenhos sem padrão. Precisa de vários DXF reais como casos de teste. |
| 5 | Encontros L/T/X, compensadores, validações | **Alta** | Aqui moram as "exceções". Precisa das regras escritas pelo engenheiro. |
| 6 | Quantitativo, relatório, multi-versão | Média | Quantitativo é fácil; o multi-target .NET 8 é trabalho mecânico. |
| + | Elevações (verga e contraverga) | **Alta** | É um segundo produto. Estimar só depois do MVP em uso. |

**Estimativa:** a do resumo (MVP em cerca de 10 sessões, completo em 20–30) é razoável. O
auto-update deve encurtar bastante o ciclo de teste, porque o engenheiro não precisa mais compilar
nada.

---

## 4. Riscos

| Risco | Probabilidade | Mitigação |
|---|---|---|
| Regras de engenharia não escritas | Alta | Lista de perguntas abaixo; casos de teste com paredes reais. |
| Desenhos de arquitetos sem padrão | Alta | Detecção assistida; configurar layers/blocos por obra. |
| TI bloqueando DLL não assinada / download | Média | Bundle em pasta confiável; assinar a DLL no futuro se for preciso. |
| AutoCAD LT na equipe | Baixa? | Confirmar. |
| Repositório público expor o código | — | Decisão do item 4. |

---

## 5. Perguntas para o engenheiro

1. Para 604 cm, qual composição está certa? (ver item 7) Bolacha de 4 é aceitável em parede comum?
2. Quando o 34 e o 54 devem ser usados? O 54 pode aparecer fora de encontro?
3. Qual é a sobreposição mínima entre juntas de fiadas consecutivas (5, 9, 10 ou 20 cm)?
4. A modulação usa o comprimento do eixo ou da face da parede?
5. Os blocos `P-Bloco39` etc. têm o ponto de inserção onde? Estão desenhados em cm?
6. Quais nomes de bloco e de layer são os corretos: os da seção 5 ou os da seção 6 do resumo?
7. A 2ª fiada deve ser desenhada sobre a 1ª (em outro layer) ou deslocada?
8. As peças habilitadas por obra devem ficar gravadas no DWG?
9. Alguém usa AutoCAD LT ou versão anterior a 2022?
10. Pode mandar 2 ou 3 DWG/DXF reais (planta + modulação feita à mão) para virar caso de teste?
