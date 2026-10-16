# Análise do resumo do projeto (resumo-plugin-modulacao-alvenaria.md)

Análise crítica do documento de contexto vindo da conversa anterior: falhas, lacunas,
viabilidade e dificuldade. Os itens marcados com **[decidir]** precisam de resposta do
engenheiro antes da fase correspondente.

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
