using System;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Plugin.Servicos;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AlvMod.Plugin
{
    /// <summary>Obra (família, peças usadas, unidade, escala, preços) e catálogo de peças.</summary>
    public sealed partial class Comandos
    {
        /// <summary>Contexto pronto para modular: família já filtrada pelas peças da obra.</summary>
        private sealed class ContextoObra
        {
            public CatalogoBlocos Catalogo;
            public ConfigObra Config;
            public Familia Familia;
        }

        [CommandMethod("ALVOBRA")]
        public void ConfigurarObra()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var catalogo = CatalogoLocal.Carregar();
            var cfg = LerConfig(doc);
            if (!PerguntarObra(doc.Editor, catalogo, cfg)) return;
            if (PerguntarUnidadeEEscala(doc.Editor, cfg)) GravarConfig(doc, cfg);
        }

        /// <summary>
        /// Lê a configuração do DWG; se a obra ainda não foi configurada, pergunta (questionário, item 52:
        /// perguntar a família antes de modular). Sempre pergunta unidade e escala (itens 37 e 41).
        /// </summary>
        private static ContextoObra PrepararObra(Document doc)
        {
            var ed = doc.Editor;
            var ctx = new ContextoObra { Catalogo = CatalogoLocal.Carregar(), Config = LerConfig(doc) };

            var familia = ctx.Config.Configurada ? ctx.Catalogo.Buscar(ctx.Config.Familia) : null;
            if (familia == null)
            {
                if (ctx.Config.Configurada)
                    ed.WriteMessage($"\nA família \"{ctx.Config.Familia}\" desta obra não existe mais no catálogo.");
                if (!PerguntarObra(ed, ctx.Catalogo, ctx.Config)) return null;
            }
            else
            {
                ed.WriteMessage($"\nObra: família {ctx.Config.Familia}" +
                                (ctx.Config.PecasDesabilitadas.Count > 0 ? $", sem as peças {string.Join(", ", ctx.Config.PecasDesabilitadas)}" : "") +
                                ". (ALVOBRA para mudar)");
            }

            if (!PerguntarUnidadeEEscala(ed, ctx.Config)) return null;
            GravarConfig(doc, ctx.Config);

            ctx.Familia = ctx.Catalogo.Buscar(ctx.Config.Familia).ParaObra(ctx.Config.PecasDesabilitadas);
            if (!ctx.Familia.PecasDeFiada().Any())
            {
                ed.WriteMessage("\nTodas as peças da família estão desabilitadas nesta obra. Use ALVOBRA.");
                return null;
            }
            return ctx;
        }

        private static bool PerguntarObra(Editor ed, CatalogoBlocos catalogo, ConfigObra cfg)
        {
            var familia = Perguntas.EscolherFamilia(ed, catalogo, cfg.Familia);
            if (familia == null) return false;
            if (familia.Nome != cfg.Familia) cfg.PecasDesabilitadas.Clear();
            cfg.Familia = familia.Nome;

            var pecas = familia.Pecas.Where(p => p.Funcao != FuncaoPeca.Canaleta).ToList();
            ed.WriteMessage("\nPeças da família: " + string.Join(", ", pecas.Select(p =>
                p.Codigo + (p.Habilitada ? "" : " (desligada no catálogo)"))));
            var fora = Perguntas.Texto(ed,
                "\nCódigos das peças que a construtora NÃO usa nesta obra, separados por vírgula (- = nenhuma)",
                cfg.PecasDesabilitadas.Count > 0 ? string.Join(",", cfg.PecasDesabilitadas) : "-");
            if (fora == null) return false;
            cfg.PecasDesabilitadas = fora == "-"
                ? new System.Collections.Generic.List<string>()
                : fora.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(c => familia.Buscar(c) != null).Select(c => familia.Buscar(c).Codigo).Distinct().ToList();

            var precos = Perguntas.SimNao(ed, "\nInformar preços dos blocos (para o quantitativo)?", false);
            if (precos == null) return false;
            if (precos.Value)
            {
                foreach (var p in pecas.Where(p => !cfg.PecasDesabilitadas.Contains(p.Codigo)))
                {
                    cfg.Precos.TryGetValue(p.Codigo, out var atual);
                    var v = Perguntas.Numero(ed, $"\nPreço unitário do bloco {p.Codigo} ({p.NomeBlocoDwg}), 0 = sem preço", atual, permiteZero: true);
                    if (v == null) return false;
                    if (v.Value > 0) cfg.Precos[p.Codigo] = v.Value;
                    else cfg.Precos.Remove(p.Codigo);
                }
            }
            return true;
        }

        private static bool PerguntarUnidadeEEscala(Editor ed, ConfigObra cfg)
        {
            var fator = Perguntas.Numero(ed, "\nQuantos cm vale 1 unidade do desenho? (1 = cm, 100 = metro, 0.1 = mm)", cfg.FatorCm);
            if (fator == null) return false;
            var escala = Perguntas.Numero(ed, "\nEscala de impressão 1:", cfg.Escala);
            if (escala == null) return false;
            cfg.FatorCm = fator.Value;
            cfg.Escala = escala.Value;
            return true;
        }

        private static ConfigObra LerConfig(Document doc)
        {
            using (var tr = doc.Database.TransactionManager.StartTransaction())
                return ObraNoDesenho.Ler(doc.Database, tr);
        }

        private static void GravarConfig(Document doc, ConfigObra cfg)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                ObraNoDesenho.Gravar(doc.Database, tr, cfg);
                tr.Commit();
            }
        }

        // ------------------------------------------------------------------------------------------
        // Catálogo (questionário, itens 9, 14 e 56: o usuário cadastra famílias e blocos livremente)
        // ------------------------------------------------------------------------------------------

        [CommandMethod("ALVCATALOGO")]
        public void EditarCatalogo()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            var catalogo = CatalogoLocal.Carregar();
            ed.WriteMessage($"\nCatálogo: {CatalogoLocal.Caminho}");

            while (true)
            {
                var acao = Perguntas.Opcao(ed, "\nCatálogo [Listar/Adicionar/Editar/Remover/Familia/Sair]",
                    new[] { "Listar", "Adicionar", "Editar", "Remover", "Familia", "Sair" }, "Sair");
                if (acao == null || acao == "Sair") return;

                bool mudou = false;
                switch (acao)
                {
                    case "Listar": Listar(ed, catalogo); break;
                    case "Adicionar": mudou = EditarPeca(ed, catalogo, nova: true); break;
                    case "Editar": mudou = EditarPeca(ed, catalogo, nova: false); break;
                    case "Remover": mudou = RemoverPeca(ed, catalogo); break;
                    case "Familia": mudou = NovaFamilia(ed, catalogo); break;
                }
                if (mudou)
                {
                    catalogo.Salvar(CatalogoLocal.Caminho);
                    ed.WriteMessage("\nCatálogo salvo.");
                }
            }
        }

        private static void Listar(Editor ed, CatalogoBlocos catalogo)
        {
            var f = Perguntas.EscolherFamilia(ed, catalogo, null);
            if (f == null) return;
            ed.WriteMessage($"\n{f.Nome} — {f.Material}, junta {f.Junta} cm");
            ed.WriteMessage("\n  Código | Dimensões | Função | Bloco DWG | Layer | Cor | Custo | Grauteável | Ativa");
            foreach (var p in f.Pecas)
                ed.WriteMessage($"\n  {p.Codigo} | {p.Dimensoes} | {p.Funcao} | {p.NomeBlocoDwg} | {p.Layer} | {p.Cor} | {p.Custo} | " +
                                $"{(p.Grauteavel ? "sim" : "não")} | {(p.Habilitada ? "sim" : "não")}");
        }

        private static bool EditarPeca(Editor ed, CatalogoBlocos catalogo, bool nova)
        {
            var f = Perguntas.EscolherFamilia(ed, catalogo, null);
            if (f == null) return false;

            Peca p;
            if (nova)
            {
                var codigo = Perguntas.Texto(ed, "\nCódigo da peça (ex.: 29, C19)");
                if (string.IsNullOrEmpty(codigo)) return false;
                if (f.Buscar(codigo) != null) { ed.WriteMessage("\nJá existe uma peça com esse código. Use Editar."); return false; }
                var comp = Perguntas.Inteiro(ed, "\nComprimento real (cm)", 39, 1);
                if (comp == null) return false;
                p = new Peca { Codigo = codigo, Comprimento = comp.Value, Largura = 14, Altura = 19 };
            }
            else
            {
                var codigo = Perguntas.Texto(ed, "\nCódigo da peça a editar");
                p = codigo == null ? null : f.Buscar(codigo);
                if (p == null) { ed.WriteMessage("\nPeça não encontrada."); return false; }
                var comp = Perguntas.Inteiro(ed, "\nComprimento real (cm)", p.Comprimento, 1);
                if (comp == null) return false;
                p.Comprimento = comp.Value;
            }

            var larg = Perguntas.Inteiro(ed, "\nLargura (cm)", p.Largura, 1);
            if (larg == null) return false;
            var alt = Perguntas.Inteiro(ed, "\nAltura (cm)", p.Altura, 1);
            if (alt == null) return false;
            var funcao = Perguntas.Opcao(ed, "\nFunção [Principal/Meio/Amarracao/Encontro/Compensador/Canaleta]",
                Enum.GetNames(typeof(FuncaoPeca)), (nova ? FuncaoPeca.Principal : p.Funcao).ToString());
            if (funcao == null) return false;
            p.Largura = larg.Value;
            p.Altura = alt.Value;
            p.Funcao = (FuncaoPeca)Enum.Parse(typeof(FuncaoPeca), funcao);

            string sufixoDim = $"{p.Comprimento}X{p.Largura}X{p.Altura}";
            var bloco = Perguntas.Texto(ed, "\nNome do bloco DWG em planta", nova ? $"P-{sufixoDim}" : p.NomeBlocoDwg);
            if (bloco == null) return false;
            var layer = Perguntas.Texto(ed, "\nLayer", nova ? $"BLOCO {p.Largura}X{p.Comprimento}" : p.Layer);
            if (layer == null) return false;
            var cor = Perguntas.Inteiro(ed, "\nCor do layer (1–255)", nova ? 7 : p.Cor, 1);
            if (cor == null) return false;
            var custo = Perguntas.Inteiro(ed, "\nCusto para o motor (menor = preferida; 39 = 10, 34 = 20, compensadores 22–24)",
                nova ? CustoSugerido(p.Funcao) : p.Custo);
            if (custo == null) return false;
            var graute = Perguntas.SimNao(ed, "\nGrauteável?", nova ? p.Funcao != FuncaoPeca.Compensador : p.Grauteavel);
            if (graute == null) return false;
            var ativa = Perguntas.SimNao(ed, "\nAtiva no catálogo?", nova || p.Habilitada);
            if (ativa == null) return false;

            p.NomeBlocoDwg = bloco;
            p.Layer = layer;
            p.Cor = (short)Math.Min(255, cor.Value);
            p.Custo = custo.Value;
            p.Grauteavel = graute.Value;
            p.Habilitada = ativa.Value;
            if (nova) f.Pecas.Add(p);
            return true;
        }

        private static int CustoSugerido(FuncaoPeca f) => f switch
        {
            FuncaoPeca.Principal => 10,
            FuncaoPeca.Meio => 12,
            FuncaoPeca.Encontro => 14,
            FuncaoPeca.Amarracao => 20,
            FuncaoPeca.Compensador => 23,
            _ => 0
        };

        private static bool RemoverPeca(Editor ed, CatalogoBlocos catalogo)
        {
            var f = Perguntas.EscolherFamilia(ed, catalogo, null);
            if (f == null) return false;
            var codigo = Perguntas.Texto(ed, "\nCódigo da peça a remover");
            var p = codigo == null ? null : f.Buscar(codigo);
            if (p == null) { ed.WriteMessage("\nPeça não encontrada."); return false; }
            if (Perguntas.SimNao(ed, $"\nRemover a peça {p.Codigo} ({p.NomeBlocoDwg}) da família {f.Nome}?", false) != true) return false;
            f.Pecas.Remove(p);
            return true;
        }

        private static bool NovaFamilia(Editor ed, CatalogoBlocos catalogo)
        {
            var nome = Perguntas.Texto(ed, "\nNome da nova família (ex.: CONCRETO-29)");
            if (string.IsNullOrEmpty(nome)) return false;
            if (catalogo.Buscar(nome) != null) { ed.WriteMessage("\nJá existe uma família com esse nome."); return false; }
            var material = Perguntas.Opcao(ed, "\nMaterial [Concreto/Ceramico]", new[] { "Concreto", "Ceramico" }, "Concreto");
            if (material == null) return false;
            var junta = Perguntas.Inteiro(ed, "\nJunta (cm)", 1, 0);
            if (junta == null) return false;
            catalogo.Familias.Add(new Familia
            {
                Nome = nome.ToUpperInvariant(),
                Material = material == "Ceramico" ? Material.Ceramico : Material.Concreto,
                Junta = junta.Value
            });
            ed.WriteMessage("\nFamília criada. Use Adicionar para cadastrar as peças.");
            return true;
        }
    }
}
