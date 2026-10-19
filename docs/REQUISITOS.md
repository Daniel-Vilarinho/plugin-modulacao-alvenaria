# AlvMod: requisitos do cliente e onde cada um está no código

Fonte de verdade das regras do plugin. Cada requisito tem a **origem**, **onde está implementado** e
**qual teste automático prova** que ele vale. Para mudar uma regra, mude a linha daqui, o código
indicado e o teste, nessa ordem.

Legenda da origem:
- **Q** = questionário do engenheiro (número do item);
- **E1** = respostas depois do questionário (nomes, layers, inserção, folgas, cotas);
- **C1**, **C2**, **C3** = 1ª, 2ª e 3ª rodada de correções depois de testar no AutoCAD.

Os testes ficam em `tests/AlvMod.Core.Tests`:
- `Mod` = `ModuladorTests`;
- `Sint` = `PlantaSinteticaTests` (cômodo de 400 × 300 com as medidas calculadas à mão);
- `Real` = `PlantaRealModulacaoTests` / `PlantaRealTests` (planta do engenheiro, só no PC de
  desenvolvimento);
- `Cat` = `CatalogoESaidasTests`.

"AutoCAD" na coluna de teste = só dá para conferir dentro do AutoCAD (roteiro de teste).

---

## 1. Blocos e família

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| B1 | Família de **concreto 39**: blocos 39, 34, 19, 54 e bolachas 9 e 4 (largura 14, altura 19). | Q9, Q11, E1 | `CatalogoPadrao.Familia39Concreto` | `Cat.Ordem_de_prioridade_do_catalogo_padrao_*` |
| B2 | Nomes `P-Bloco39`, `P-Bloco34`, `P-Bloco19`, `P-Bloco54`, `P-Bloco9`, `P-Bloco4`. | Q53, E1 | `CatalogoPadrao.Nova` | `Cat.Catalogo_da_v02_e_migrado_*` |
| B3 | Layers `P-Bloco 14x39`… com as cores da legenda (74, 170, 204, 30, 8, 8). | Q44, Q45, E1 | `CatalogoPadrao`, `Desenhista.GarantirLayer` | `Cat.Catalogo_da_v02_e_migrado_*` |
| B4 | Junta de **1 cm** entre **todos** os blocos (sem espaços de 1,5 ou 5,5 cm). | Q10, E1, C1-4, C2-3 | `Modulador.Posicionar`, `PosicionarAjustado` | `Mod.Diferenca_de_desenho_ate_meio_cm_*`, `Real.Regras_do_engenheiro_*` |
| B5 | Ponto de inserção do bloco no **canto inferior esquerdo**; o tamanho real do bloco do DWG é medido. | E1 | `Desenhista.Desenhar` | AutoCAD |
| B6 | O usuário cadastra blocos e famílias (o catálogo muda de obra para obra). | Q14, Q52, Q56 | `ALVCATALOGO`, `CatalogoBlocos` (JSON) | `Cat.Catalogo_e_criado_*`, `Cat.Json_editado_a_mao_*` |
| B7 | Desligar, **por obra**, as peças que a construtora não usa. | Q15 | `ALVOBRA`, `ConfigObra.PecasDesabilitadas`, `Familia.ParaObra` | `Mod.Peca_desabilitada_*`, `Mod.Pecas_desabilitadas_na_obra_*` |
| B8 | Bolachas (4 e 9) **não são grauteáveis**. | Q13 | `Peca.Grauteavel` | `CatalogoPadrao` |
| B9 | Peça nova no catálogo recebe o custo na mesma escala do padrão. | revisão | `CatalogoPadrao.CustoSugerido` | `Cat.Custo_sugerido_no_ALVCATALOGO_*` |

## 2. Onde cada peça pode entrar

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| P1 | **Priorizar sempre o 39** (o mais barato), respeitando as outras regras. | Q21, C1-5 | custos do catálogo (39 = 10) | `Mod.Priorizar_39_e_usar_34_so_em_segundo_lugar` |
| P2 | **34** recomendado nos **cantos**; no meio da parede só para adaptar, em 2º lugar. | C1-6 | `Pontas.Passa34`; custo 45 | `Mod.Canto_parede_que_passa_*`, `Mod.Priorizar_39_*` |
| P3 | Melhor **alguns compensadores** do que a parede **cheia de 34**. | Q20 | custos 34 = 45, 9 = 80 (no máximo três 34 seguidos) | `Mod.Seiscentos_e_quatro_prefere_um_compensador_*` |
| P4 | **19 só na ponta de parede sem continuidade** (porta ou parede solta). Nunca no meio, em canto, T ou cruz. | C1-3, C2-1, C2-7 | `Modulador.Resolver` (caso `Meio`), `Pontas.Encontro` | `Mod.Nunca_19_em_encontro_*`, `Mod.Regras_de_posicao_*`, `Real.Regras_do_engenheiro_*` |
| P5 | **Parede que acaba na porta acaba em 20** (19 + junta). | C1-2 | `Pontas.Porta` (19 obrigatório) | `Mod.Ponta_que_encosta_em_porta_termina_em_19`, `Mod.Porta_nas_duas_pontas_*` |
| P6 | **54 só em encontro em T e em cruz**, e **todo T e toda cruz tem 54** (dois T vizinhos dividem um). | C1-1, C2-2, C2-5 | `PlanejadorFiadas.Planejar` (54 fixo centrado), `AgruparEncontros` | `Mod.Motor_nunca_escolhe_o_54_*`, `Sint.T_recebe_54_*`, `Real.Todo_T_e_toda_cruz_tem_bloco_54` |
| P7 | **Bolachas nunca em canto, encontro ou ponta de porta**: vão para o meio da parede. | Q34, C1-7 | `Pontas.SemCompensador`; penalidade perto das pontas em `Resolver` | `Mod.Compensador_nunca_encosta_em_porta`, `Mod.Pecas_de_ajuste_ficam_no_meio_*` |
| P8 | Blocos **alinhados**: meio bloco de defasagem entre as fiadas nos encontros. | Q18, C2-4, C2-6 | padrão de encontro (§3) | `Sint.T_com_54_deixa_meio_bloco_*` |

## 3. Encontros (amarração direta)

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| E1 | Amarração **direta**, alternando a parede que passa a cada fiada. | Q16 | `Encontro.Dominante` | `Sint.Fiada_2_alterna_*` |
| E2 | Passa primeiro **a parede maior** (no T, a contínua). | Q17 | `Encontro.Dominante` | `Sint.Fiada_1_parede_maior_passa_nos_cantos_*` |
| E3 | **Canto (L)**: a que passa vai até a face externa e termina em **34**; a outra encosta a 1 cm e começa com **39**. | C1-6, C2 | `PlanejadorFiadas.Planejar`, `Pontas.Passa34/Exige39` | `Mod.Canto_parede_que_passa_*`, `Sint.Fiada_1_*` |
| E4 | **T**: na fiada em que a contínua passa, **54 centrado** e a que chega encosta com 39. Na outra fiada, a que chega atravessa e termina em **34**; a contínua é cortada e encosta com 39. | C2-2 | `PlanejadorFiadas.Planejar` | `Sint.T_recebe_54_*`, `Sint.T_com_54_*` |
| E5 | **Cruz**: 54 centrado na parede que passa naquela fiada (alternando); a outra é cortada e encosta com 39. | C2-2 | `PlanejadorFiadas.Planejar` | `Real.Todo_T_e_toda_cruz_tem_bloco_54` |
| E6 | Parede contínua que o desenho partiu no encontro é tratada como **uma parede que passa**. | revisão | `DetectorParedes.UnirParedesPartidas` | `Sint.Parede_partida_pelo_desenho_no_T_*` |
| E7 | Encontro que o detector não reconhece: a ponta não recebe 19 nem bolacha e vai **aviso** para o relatório. | revisão | `PlanejadorFiadas.Planejar` (`TipoEncontro.Outro`) | — |

## 4. Aberturas

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| A1 | Porta: **tamanho da folha (lido pelo arco) + 11 cm**, centrada no vão desenhado. | Q30, E1, C1-8 | `DetectorParedes.TamanhoPorta`, `Vao.Alvenaria` | `Sint.Porta_com_arco_*`, `Sint.Porta_sem_arco_*`, `Real.Portas_sao_lidas_*` |
| A2 | Janela: **tamanho do vidro + 6 cm**. | Q30, C1-8 | `DetectorParedes.TamanhoJanela` | `Sint.Janela_nao_corta_a_fiada_*` |
| A3 | As folgas (11 e 6) podem ser mudadas por obra. | E1 | `ALVOBRA`, `ConfigObra.FolgaPorta/FolgaJanela` | `Cat.Folga_de_marco_padrao_*` |
| A4 | Porta deixa o vão **livre na 1ª e na 2ª fiada**. | Q31 | `Parede.VaosQueCortam` | `Sint.Fiada_1_*_porta_soma_folga_de_marco` |
| A5 | Janela **não corta** a 1ª e a 2ª fiada, mas **entra na cota**. Vale também no `ALV1FIADA`. | Q32, Q33, Q47 | `VaosQueCortam`, `PontosDeCota`; `ALV1FIADA` une os segmentos | `Sint.Janela_nao_corta_a_fiada_*` |
| A6 | Portas e janelas vêm de qualquer jeito: **detecção assistida** e conferida antes de modular. | Q27, Q28 | `ALVPLANTA`: clicar uma linha de cada tipo, eixos e números desenhados, pergunta antes de modular | AutoCAD |

## 5. Leitura do desenho

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| L1 | Ler as paredes desenhadas pelas duas faces e **trabalhar no eixo**. | Q24, Q37, Q38 | `DetectorParedes` | `Sint.Detecta_cinco_paredes_*`, `Sint.Encontros_quatro_L_*`, `Real.Detecta_paredes_*` |
| L2 | **Sempre perguntar a unidade e a escala** (o cabeçalho do DWG não é confiável). | Q37, Q41 | `PrepararObra` | AutoCAD |
| L3 | Unidade errada: em vez de só "nenhuma parede", o plugin **sugere a unidade** que encontra as paredes. | teste no AutoCAD (unidade 0,02) | `DetectorParedes.SugerirFatorCm` | `Sint.Unidade_errada_*` |
| L4 | Não existe padrão de layers: cada arquiteto desenha de um jeito. | Q39, Q42 | layers escolhidos clicando, gravados na obra | AutoCAD |
| L5 | A arquitetura limpa vem num bloco, em metros: ler sem explodir. | arquivos reais | `LeitorPlanta` (explode na memória) | `Real.Detecta_paredes_*` |
| L6 | Paredes inclinadas existem, mas são raras: aviso e `ALV1FIADA`. | Q25 | aviso em `DetectorParedes.Detectar` | — |

## 6. Saída

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| S1 | 1ª fiada primeiro; o projetista confere e edita; a 2ª fiada é feita **em cima da 1ª editada**. | Q69, Q70 | `ALVPLANTA`, `ALVPLANTA2` (lê os blocos desenhados) | AutoCAD |
| S2 | 1ª e 2ª fiada no **mesmo arquivo**, sem separar por layer. | Q46 | deslocamento até a planta da 2ª fiada | AutoCAD |
| S3 | 2ª fiada sem junta a prumo: defasagem **mínima de 1/4 de bloco**, ideal **meio bloco**. Se não houver jeito, faz com aviso. | Q18, Q19 | `OpcoesModulacao`, `Modulador.Tentativas` | `Mod.Segunda_fiada_*`, `Sint.Pavimento_modula_*`, `Real.Sem_sobreposicao_*` |
| S4 | Parede que **não fecha**: não forçar; colocar as peças a partir do encontro e deixar a sobra **do lado da porta**; dizer o comprimento que fecharia. | Q22, C1 | `Modulador.Modular` (preenchimento parcial) | `Mod.Seiscentos_cm_nao_fecha_*`, `Mod.Sobra_vai_para_o_lado_da_porta_*` |
| S5 | **Bonecas** curtas onde nenhum bloco cabe: ficam **manuais** e vão para o relatório. | C3 | `ComandosPlanta.ModularEDesenhar` | `Real.Regras_do_engenheiro_*` |
| S6 | **Numeração**: horizontais de cima para baixo, depois verticais da esquerda para a direita; sufixo -A/-B quando a porta divide a parede. | Q48 | `Numeracao.Numerar`, `PlanejadorFiadas.NomeDoSegmento` | `Sint.Numeracao_*` |
| S7 | **Cotas pela face**, em cadeia: parede, vão, parede. | Q47, E1 | `PlanejadorFiadas.PontosDeCota`, `Desenhista.CotarPontos` | `Sint.Janela_nao_corta_a_fiada_*` |
| S8 | **Quantitativo** de blocos para o Excel, com preço quando informado. | Q7, Q49, Q54 | `ALVQUANT`, `Quantitativo` | `Cat.Quantitativo_*` |
| S9 | **Relatório** em Word com as paredes que precisam de ajuste. | Q50 | `ALVRELATORIO`, `RelatorioDocx` | `Cat.Relatorio_docx_*` |
| S10 | **Memória de cálculo** no relatório: por trecho, o comprimento, as pontas, as peças em ordem e se fechou. | auditoria | `MemoriaCalculo`, `Registro` | `Cat.Memoria_de_calculo_*` |
| S11 | Rodar o `ALVPLANTA` de novo **não duplica** nada: oferece apagar a modulação anterior, e o relatório troca o resultado antigo pelo novo. | revisão | `ComandosPlanta.OferecerApagarAnterior`, `Registro.Reiniciar` | AutoCAD |
| S12 | Resultado **editável** à mão (blocos e cotas comuns do AutoCAD). | Q51 | `Desenhista` | AutoCAD |

## 7. Ambiente

| ID | Requisito | Origem | Implementação |
|---|---|---|---|
| X1 | Interface **só por linha de comando**. | Q65 | todos os comandos |
| X2 | AutoCAD 2018 em diante (LT não aceita plugin). | Q59 | net48 (2018–2024) + net8 (2025+) |
| X3 | Atualização **dentro do plugin** ("Atualizar agora? Sim/Não"). | pedido inicial, Q73 | `Atualizador`, `App`, GitHub Releases |
| X4 | Até 3 máquinas locais, sem política de TI. | Q62, Q63 | bundle em `%APPDATA%` |

---

## Interpretações a confirmar com o engenheiro

1. **54 no T em uma fiada só.** O 54 entra na fiada em que a parede contínua passa. Na outra fiada,
   quem passa é a parede que chega (termina em 34) e a contínua é cortada. A correção C2-2 diz "T e
   cruz sempre com 54": se ele quer o 54 **nas duas fiadas**, a regra muda em
   `PlanejadorFiadas.Planejar`.
2. **Cantos de comprimento parecido.** No L, passa primeiro a parede **maior**. Se ele preferir outro
   critério (ex.: sempre a horizontal na 1ª fiada), a regra está em `Encontro.Dominante`.

## Fora do escopo atual

- Elevações das paredes com verga e contraverga (Q6, Q32, Q33).
- Prumadas de graute marcadas no desenho (Q26).
- Leitura automática de paredes inclinadas.
