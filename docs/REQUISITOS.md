# AlvMod: requisitos do cliente e onde cada um está no código

Fonte de verdade das regras do plugin. Cada requisito tem a **origem**, **onde está implementado** e
**qual teste automático prova** que ele vale. Para mudar uma regra, mude a linha daqui, o código
indicado e o teste, nessa ordem.

Legenda da origem:
- **Q** = questionário do engenheiro (número do item);
- **E1** = respostas depois do questionário (nomes, layers, inserção, folgas, cotas);
- **C1**, **C2**, **C3** = 1ª, 2ª e 3ª rodada de correções depois de testar no AutoCAD;
- **R** = modulação de referência: um pavimento real que o engenheiro modulou à mão (a arquitetura limpa
  que ele recebe e as duas fiadas que ele entrega), comparado peça por peça com o plugin. Quando R
  contradiz uma interpretação anterior, vale R (a linha diz o que foi substituído).

Os testes ficam em `tests/AlvMod.Core.Tests`:
- `Mod` = `ModuladorTests` (um trecho de parede);
- `Det` = `DetectorTests` (leitura da arquitetura);
- `Plan` = `PlanejadorTests` (pavimento: encontros, portas, simetria, cotas, numeração);
- `Ref` = `PlantaReferenciaTests` (o pavimento de referência; os arquivos ficam só na máquina de
  desenvolvimento, fora do repositório);
- `Cat` = `CatalogoESaidasTests` (catálogo, obra, quantitativo, relatório).

"AutoCAD" na coluna de teste = só dá para conferir dentro do AutoCAD (roteiro de teste).

---

## 1. Blocos e família

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| B1 | Família de **concreto 39**: blocos 39, 34, 19, 54 e bolachas 9 e 4 (largura 14, altura 19). | Q9, Q11, E1 | `CatalogoPadrao.Familia39Concreto` | `Cat.Ordem_de_prioridade_do_catalogo_padrao_e_39_34_19_e_bolachas_por_ultimo` |
| B2 | Blocos com os nomes do engenheiro: `P-Bloco39`, `P-Bloco 34`, `P-Bloco 19`, `P-Bloco 54`, `Bloco 9x14` e `bolacha`. O bloco do desenho é achado sem diferenciar espaço, hífen e maiúscula. | E1, R | `CatalogoPadrao.Nova`, `Familia.BuscarPorBloco` | `Cat.Catalogo_v5_passa_para_os_nomes_e_layers_da_modulacao_do_engenheiro`, `Cat.Bloco_do_desenho_e_reconhecido_sem_diferenciar_espaco_hifen_e_maiuscula` |
| B3 | Layers `P-Bloco 14x39`, `P-Bloco 14x34`, `P-Bloco 14x19`, `P-Bloco 14x54` e `BOLACHA 14X4` (as duas bolachas), cores 74, 170, 204, 30 e 8. Layer que já existe mantém a cor do escritório. | Q44, Q45, E1, R | `CatalogoPadrao`, `Camadas.Garantir` | `Cat.Catalogo_v5_passa_para_os_nomes_e_layers_da_modulacao_do_engenheiro` |
| B4 | Junta de **1 cm** entre **todos** os blocos. | Q10, E1, C1-4, C2-3 | `Modulador.Posicionar` | `Mod.Quinhentos_e_noventa_e_nove_entre_encontros_sao_quinze_blocos_de_39_a_1cm`, `Plan.Todos_os_trechos_fecham_com_juntas_de_1cm` |
| B5 | Inserção pelo **canto inferior esquerdo** do desenho real do bloco. Bloco em metro ou noutra escala é normal (o do engenheiro é em metro); bolacha desenhada "em pé" (14 × 4) é girada; só avisa quando a **proporção** do bloco não bate com a peça. | E1, R | `Desenhista.Desenhar`, `Desenhista.Medir` | AutoCAD |
| B6 | O usuário cadastra blocos e famílias (o catálogo muda de obra para obra). | Q14, Q52, Q56 | `ALVCATALOGO`, `CatalogoBlocos` (JSON) | `Cat.Catalogo_e_criado_na_primeira_vez_e_relido_igual`, `Cat.Json_editado_a_mao_sem_campos_opcionais_usa_os_padroes` |
| B7 | Desligar, **por obra**, as peças que a construtora não usa. | Q15 | `ALVOBRA`, `ConfigObra.PecasDesabilitadas`, `Familia.ParaObra` | `Mod.Peca_desabilitada_nunca_e_usada` |
| B8 | Bolachas (4 e 9) **não são grauteáveis**. | Q13 | `Peca.Grauteavel` | `CatalogoPadrao` |
| B9 | Peça nova no catálogo recebe o custo na mesma escala do padrão. | revisão | `CatalogoPadrao.CustoSugerido` | `Cat.Custo_sugerido_no_ALVCATALOGO_e_o_mesmo_do_catalogo_padrao` |
| B10 | Catálogo de versão antiga passa para os nomes, layers e custos novos **sem perder o que o usuário editou**. | revisão | `CatalogoBlocos` (migração até a v6) | `Cat.Catalogo_da_v02_e_migrado_para_os_nomes_reais_sem_perder_edicoes`, `Cat.Custos_da_v3_migram_para_a_nova_prioridade_e_edicoes_do_usuario_ficam` |

## 2. Onde cada peça pode entrar

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| P1 | **Priorizar o 39.** Custos do motor: 39 = 10, 54 = 10, 34 = 45, 19 = 60, bolacha 9 = 150, bolacha 4 = 160. | Q21, C1-5, R | custos do catálogo | `Cat.Ordem_de_prioridade_do_catalogo_padrao_e_39_34_19_e_bolachas_por_ultimo` |
| P2 | Os **34 ficam juntos (até quatro seguidos) numa ponta do trecho**, nunca no meio. A ponta é, nesta ordem: a que **passa** no encontro, a que é **cortada** pela parede que chega no T, a que **encosta**. Na 1ª fiada, parede que passa nas duas pontas leva um 34 em cada (os extras vão para a ponta mais longe do centro do prédio). Empate: a ponta mais perto do centro. *Substitui C1-6 ("34 no meio só para adaptar").* | R | `Pontas.Ajuste`, `PlanejadorFiadas.ParaPontas` | `Mod.Trinta_e_quatro_ficam_juntos_na_ponta_de_ajuste_e_nao_no_meio` |
| P3 | **Melhor vários 34 do que bolachas** (quatro 34 em vez de duas bolachas). | Q20, R | custos (P1) | `Mod.Prefere_quatro_34_a_duas_bolachas` |
| P4 | **19 só na ombreira de porta ou na ponta solta**, alternando **19 e 39** entre as fiadas na mesma ombreira. Num encontro, só quando é a peça única de uma boneca ao lado de porta. Nunca no meio da parede. *Substitui C1-2 ("19 obrigatório nas duas fiadas").* | C1-3, C2-1, C2-7, R | `Modulador.Resolver`, `Pontas.Porta` | `Mod.Meio_bloco_so_na_ombreira_de_porta`, `Mod.Ombreira_de_porta_alterna_19_e_39_entre_as_fiadas`, `Mod.Boneca_entre_porta_e_encontro_aceita_a_peca_unica` |
| P5 | **54 é peça fixa**, centrada no encontro em **T e em cruz**, na fiada em que a parede contínua fica por cima. O motor nunca escolhe o 54 sozinho; dois T vizinhos dividem um 54. | C1-1, C2-2, C2-5, R | `PlanejadorFiadas.Planejar`, `AgruparEncontros` | `Plan.Cada_T_tem_um_54_centrado_numa_das_fiadas` |
| P6 | **Bolachas (4 e 9) só encostadas num elemento do encontro** (ao lado do 54 ou da parede que atravessa) ou como a boneca inteira. Nunca no quadrado do canto, nunca no meio da parede, nunca na ponta em que a parede passa; evitam encostar no bloco do canto, mesmo custando 10 cm de defasagem. *Substitui C1-7 ("bolachas no meio da parede").* | Q34, R | `Pontas.SemCompensador`, `Pontas.Canto`, `Modulador.Resolver` | `Mod.Bolacha_so_encostada_no_encontro_nunca_no_meio`, `Mod.Bolacha_nunca_no_quadrado_do_canto`, `Mod.Bolacha_evita_encostar_no_bloco_do_canto`, `Mod.Entre_o_54_e_o_canto_vai_bolacha_4_e_39_no_canto` |
| P7 | Família 39 com junta de 1 cm fecha **de 5 em 5 cm** (comprimentos terminados em 4 ou 9). | Q10 | `Modulador.ComprimentosValidosProximos` | `Mod.Passo_da_familia_39_e_5cm` |

## 3. Encontros (amarração direta)

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| E1 | Em cada encontro **uma direção fica por cima** em cada fiada e **inverte** na outra. | Q16, R | `EstadoEncontro`, `PlanejadorFiadas.Resolver` | `Plan.Em_cada_encontro_uma_parede_passa_e_inverte_na_outra_fiada` |
| E2 | Quem fica por cima em cada encontro é escolhido para **gastar menos peças especiais no pavimento inteiro** (busca local com 43 partidas). No empate, nos T e cruzes a **vertical** fica por cima na 1ª fiada; exceção: T cuja parede que chega é uma boneca ao lado de porta (a contínua fica por cima na 1ª). *Substitui Q17 ("passa a maior").* | R | `PlanejadorFiadas.Resolver`, `Preferencia`, `BonecaNoT` | `Ref.Quem_passa_em_cada_encontro_e_o_mesmo_que_o_engenheiro_escolheu` |
| E3 | **Canto (L)**: a que passa vai até a face externa e termina em 34 (às vezes 39); a outra encosta a 1 cm. | C1-6, R | `PlanejadorFiadas.PlanejarParede` | `Mod.Canto_alternado_deixa_as_juntas_a_meio_bloco` |
| E4 | **T**: contínua por cima = **54 centrado** e a que chega encosta; a que chega por cima = atravessa até a face de fora e a contínua é **cortada**, com 1 cm de cada lado. | C2-2, R | `PlanejadorFiadas.PlanejarParede` | `Plan.Cada_T_tem_um_54_centrado_numa_das_fiadas` |
| E5 | **Cruz**: 54 na parede que está por cima; a outra é cortada. Porta encostada na cruz: um bloco comum atravessa no lugar do 54. | C2-2, R | `PlanejadorFiadas.PlanejarParede` | `Ref.Modulacao_reproduz_a_do_engenheiro` |
| E6 | Parede contínua que o desenho partiu no encontro é **uma parede só**. | revisão | `DetectorParedes.UnirParedesPartidas` | indireto: `Ref.Le_a_arquitetura_limpa_com_unidade_50_cm` |
| E7 | Encontro que o detector não reconhece: a ponta não recebe 19 nem bolacha e vai **aviso** para o relatório. | revisão | `PlanejadorFiadas.Planejar` (`TipoEncontro.Outro`) | — |
| E8 | **Ponto de graute** em todo encontro L, T e X, na mesma posição nas duas fiadas (bloco `PONTO DE GROUT PLANTA`, 8,4 cm, layer `Ponto de GROUT - PLANTA`). | R | `SolucaoPavimento.Grautes`, `Anotador.Graute` | `Plan.Pontos_de_graute_em_todos_os_encontros` |
| E9 | **Apartamentos espelhados recebem a modulação espelhada** (o prédio é achado simétrico pelos eixos de fora; portas espelhadas mesmo com o desenho alguns mm fora). | R | `Simetria.Detectar`, `PlanejadorFiadas.Resolver` | `Det.Simetria_do_pavimento_e_detectada`, `Plan.Apartamentos_espelhados_recebem_modulacao_espelhada` |

## 4. Aberturas

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| A1 | Porta: abertura = **medida nominal** (vão desenhado arredondado de 5 em 5) **+ 11**: porta de 80 → 91, de 70 → 81. *Substitui C1-8 (folha lida pelo arco + 11), que vinha de ler a arquitetura na unidade errada.* | Q30, E1, R | `Vao.Nominal`, `Vao.Alvenaria` | `Det.Porta_80_vira_abertura_de_91_e_janela_100_de_101`, `Ref.Segunda_fiada_sem_junta_a_prumo_e_portas_com_91` |
| A2 | A porta **anda até 12 cm** ao longo da parede para os pedaços dos dois lados fecharem com blocos inteiros. Do lado da **dobradiça** (centro do arco) sempre fica alvenaria; do lado do trinco a porta pode chegar na face da outra parede (o encontro passa a ser contínuo nas duas fiadas). | R | `PlanejadorFiadas.Resolver` (`MoverPortas`), `Vao.LadoDobradica` | `Plan.Porta_abre_91_e_anda_no_maximo_12_cm_para_fechar` |
| A2b | **Boneca da porta de pelo menos 5 cm** em toda porta: da face da outra parede até o vão fica 1 cm de junta + 4 de alvenaria, no mínimo; a porta anda para isso. Só em parede curta demais para ter boneca dos dois lados (ex.: 110 cm com porta de 91) a porta encosta, com aviso no relatório. | cliente | `OpcoesFiadas.BonecaMinima`, `PlanejadorFiadas.PlanejarParede` | `Plan.Porta_nunca_fica_a_menos_de_5_cm_da_outra_parede`, `Ref.Segunda_fiada_sem_junta_a_prumo_e_portas_com_91` |
| A3 | Janela: abertura = **medida + 1** (150 → 151). **Não corta** a 1ª nem a 2ª fiada, mas entra nas cotas e na anotação. *Substitui "vidro + 6".* | Q32, Q33, R | `Vao.Alvenaria`, `ConfigObra.FolgaJanela` | `Det.Porta_80_vira_abertura_de_91_e_janela_100_de_101`, `Plan.Janela_nao_corta_a_primeira_nem_a_segunda_fiada` |
| A4 | **Quadro de janelas**: para cada largura desenhada o projetista informa "medida x altura / peitoril" (a arquitetura pode ter a janela com outra medida). Fica gravado na obra. | R | `ComandosPlanta.PerguntarJanelas`, `ConfigObra.Janelas`, `Vao.MedidaInformada` | AutoCAD |
| A5 | Folgas (11 e 1), deslocamento máximo da porta (12), altura da porta (220) e numeração por tipo mudam por obra. | E1 | `ALVOBRA`, `ConfigObra` | `Cat.Folga_padrao_porta_11_janela_1_e_igual_na_obra_e_no_planejador`, `Cat.Config_da_obra_ida_e_volta` |
| A6 | Portas e janelas vêm de qualquer jeito: **detecção assistida**, conferida antes de modular. | Q27, Q28 | `ALVPLANTA`: clicar uma linha de cada tipo, eixos e números desenhados, pergunta antes de modular | AutoCAD |

## 5. Leitura do desenho

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| L1 | Ler as paredes desenhadas pelas duas faces e **trabalhar no eixo**. | Q24, Q37, Q38 | `DetectorParedes` | `Det.Sala_tem_cinco_paredes_pelo_eixo_porta_janela_e_encontros` |
| L2 | **Sempre perguntar a unidade e a escala** (o cabeçalho do DWG não é confiável). | Q37, Q41 | `PrepararObra` | AutoCAD |
| L3 | Conferir a unidade pela **espessura das paredes** (bloco 14 = parede de 14 a 20 cm) e sugerir a certa: arquitetura em 1:50 lida como metro dá paredes de 30 cm → sugere **50**. | R, teste no AutoCAD | `DetectorParedes.EspessuraSuspeita`, `SugerirFatorCm` | `Det.Arquitetura_em_escala_1_50_lida_como_metro_sugere_unidade_50`, `Ref.Le_a_arquitetura_limpa_com_unidade_50_cm` |
| L4 | Não existe padrão de layers: cada arquiteto desenha de um jeito. | Q39, Q42 | layers escolhidos clicando, gravados na obra | AutoCAD |
| L5 | A arquitetura limpa vem num bloco: ler sem explodir. | arquivos reais | `LeitorPlanta` (explode na memória) | `Ref.Le_a_arquitetura_limpa_com_unidade_50_cm` |
| L6 | Paredes inclinadas existem, mas são raras: aviso e `ALV1FIADA`. | Q25 | aviso em `DetectorParedes.Detectar` | — |
| L7 | O que está no layer de parede mas não é parede (degraus de escada, linha no meio da parede, pedaço curto, espessura fora da típica da obra) é **ignorado e listado no relatório**. | R | `DetectorParedes.Filtrar` | `Det.Degraus_de_escada_no_layer_de_parede_nao_viram_parede`, `Det.Parede_com_linha_no_meio_e_uma_parede_so` |
| L8 | Janela com o vidro em várias folhas: a janela é a união delas. | R | `DetectorParedes` (cobertura do vão) | `Det.Janela_com_vidro_em_folhas_cobre_o_vao` |
| L9 | **Paredes que não são alvenaria** (drywall, shaft): o projetista clica nelas; saem da modulação, o encontro some, Ficam gravadas na obra. *Na prática o engenheiro tira da arquitetura o que não é estrutural antes de mandar; o ALVPLANTA não pergunta mais.* | R | `DetectorParedes.AplicarExclusoes`, `ConfigObra.Exclusoes`, `ComandosPlanta.MarcarNaoAlvenaria` | `Det.Parede_que_nao_e_alvenaria_sai_e_o_encontro_desaparece` |
| L10 | **Eixos na grade de 5 cm** (a arquitetura vem com diferenças de milímetros); aviso quando precisa mover mais de 1,5 cm. | R | `GradeModular.Ajustar` | `Det.Grade_modular_poe_os_eixos_a_distancias_multiplas_de_5` |

## 6. Saída

| ID | Requisito | Origem | Implementação | Teste |
|---|---|---|---|---|
| S1 | 1ª fiada primeiro; o projetista confere e edita; a 2ª fiada é feita **em cima da 1ª editada**. | Q69, Q70 | `ALVPLANTA`, `ALVPLANTA2` (lê os blocos desenhados) | AutoCAD |
| S2 | 1ª e 2ª fiada no **mesmo arquivo**, em plantas separadas. | Q46, R | deslocamento até a planta da 2ª fiada | AutoCAD |
| S3 | 2ª fiada sem junta a prumo: defasagem **mínima de 10 cm**, buscando **meio bloco (20 cm)**; a ordem das peças da 1ª fiada é ajustada para isso. Se não houver jeito, faz com aviso. | Q18, Q19, R | `Modulador.SegundaFiada`, `ModulacaoPavimento.Modular`, `Modulador.Reordenar` | `Mod.Segunda_fiada_nao_tem_junta_a_prumo`, `Mod.Reordenar_muda_so_a_ordem_das_pecas`, `Ref.Segunda_fiada_sem_junta_a_prumo_e_portas_com_91` |
| S4 | Parede que **não fecha**: não forçar; peças a partir do encontro, sobra **do lado da porta**, e o comprimento que fecharia. | Q22, C1 | `Modulador` (preenchimento parcial) | `Mod.Nao_fecha_coloca_as_pecas_a_partir_do_encontro_e_deixa_a_sobra_na_porta`, `Mod.Custo_para_o_planejador_ignora_a_posicao_e_recusa_o_que_nao_fecha` |
| S5 | **Bonecas** (pedaço curto entre porta e encontro): com as portas andando até 12 cm e a peça única aceita (19, 34 ou bolacha), elas fecham; o que não fechar vai para o relatório. | C3, R | `PlanejadorFiadas`, `Modulador` | `Mod.Boneca_entre_porta_e_encontro_aceita_a_peca_unica`, `Plan.Todos_os_trechos_fecham_com_juntas_de_1cm` |
| S6 | **Numeração por tipo** ("Modulação TIPOS"): paredes com a mesma modulação nas duas fiadas (lida de qualquer lado) recebem o mesmo número; "PAREDE n" do lado de dentro, no layer `TX PAREDES`. Ordem: horizontais de cima para baixo, depois verticais da esquerda para a direita. Na obra dá para voltar a um número por parede. | Q48, R | `Numeracao.NumerarPorTipo`, `Numeracao.Assinatura`, `ConfigObra.NumeracaoPorTipo`, `Anotador.Rotulo` | `Plan.Numeracao_por_tipo_da_o_mesmo_numero_a_paredes_iguais` |
| S7 | **Cotas internas** por cômodo, de face a face dos blocos, em cadeia com as aberturas ("40 / 151 / 45") e o total por fora; a fachada não leva cota interna. Medidas em cm em qualquer unidade do desenho (estilo `ALVMOD 1-25`, layer `FO-Cotas`). | Q47, E1, R | `Anotacoes.CotasInternas`, `Anotador.CotasInternas`, `Camadas.Cota` | `Plan.Cotas_internas_de_face_a_face_dos_blocos_com_as_aberturas` |
| S8 | **Aberturas anotadas** no layer `ABERTURA`: retângulo do vão na alvenaria, "ABERTURA" e a medida ("91x220" na porta, "151 x 100 / 120" na janela). | R | `Anotador.Abertura`, `Anotador.MedidaJanela` | AutoCAD |
| S9 | **Título** embaixo da planta: "Planta de 1ª fiada - Modulação" (ou 2ª) e "escala 1:50" (escala padrão da obra), no layer `FO-Títulos`. | R | `Anotador.Titulo` | AutoCAD |
| S10 | **Quantitativo** de blocos para o Excel, com preço quando informado. | Q7, Q49, Q54 | `ALVQUANT`, `Quantitativo` | `Cat.Quantitativo_conta_por_bloco_e_calcula_total_com_preco` |
| S11 | **Relatório** em Word com as paredes que precisam de ajuste. | Q50 | `ALVRELATORIO`, `RelatorioDocx` | `Cat.Relatorio_docx_e_um_zip_com_document_xml_e_texto_escapado` |
| S12 | **Memória de cálculo e decisões** no relatório: por trecho, o comprimento, as pontas, as peças em ordem e se fechou; e as decisões do planejamento (portas deslocadas, eixos ajustados, simetria, pedaços ignorados). | auditoria, R | `MemoriaCalculo`, `Registro`, `ComandosPlanta.RegistrarDecisoes` | `Cat.Memoria_de_calculo_descreve_pontas_pecas_e_situacao_e_vai_no_relatorio` |
| S13 | Rodar o `ALVPLANTA` de novo **não duplica** nada: oferece apagar a modulação anterior (blocos, graute, nomes, aberturas, cotas, eixos), e o relatório troca o resultado antigo pelo novo. | revisão | `ComandosPlanta.OferecerApagarAnterior`, `Registro.Reiniciar` | AutoCAD |
| S14 | Resultado **editável** à mão (blocos, textos e cotas comuns do AutoCAD). | Q51 | `Desenhista`, `Anotador` | AutoCAD |

## 7. Ambiente

| ID | Requisito | Origem | Implementação |
|---|---|---|---|
| X1 | Interface **só por linha de comando**. | Q65 | todos os comandos |
| X2 | AutoCAD 2018 em diante (LT não aceita plugin). | Q59 | net48 (2018–2024) + net8 (2025+) |
| X3 | Atualização **dentro do plugin** ("Atualizar agora? Sim/Não"). | pedido inicial, Q73 | `Atualizador`, `App`, GitHub Releases |
| X4 | Até 3 máquinas locais, sem política de TI. | Q62, Q63 | bundle em `%APPDATA%` |

---

## Interpretações a confirmar com o engenheiro

1. **Nomenclatura das paredes** (-A/-B): o cliente pediu para não se preocupar; o plugin numera por tipo.
2. **Pontos de graute fora dos encontros** (adiado pelo cliente). Além dos encontros, ele põe alguns pontos por critério
   próprio (vão grande, ponta de parede). O plugin só marca os encontros; os outros ficam à mão.
3. **Medida das janelas.** O desenho da arquitetura pode ter a janela com outra medida (uma de 50 que é
   de 60, outra de 150 que é de 200). O plugin pergunta o quadro de janelas; sem ele, usa o desenho.
4. **Porta que falta na arquitetura limpa.** O plugin não tem como saber: os eixos e números desenhados
   antes de modular servem para conferir isso.

## Fora do escopo atual

- Elevações das paredes com verga e contraverga (Q6, Q32, Q33).
- Prumadas de graute além dos encontros (Q26).
- Leitura automática de paredes inclinadas.
