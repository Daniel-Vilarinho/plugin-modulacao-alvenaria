using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlvMod.Core.Saidas;
using AlvMod.Plugin.Atualizacao;
using AlvMod.Plugin.Servicos;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AlvMod.Plugin
{
    /// <summary>Quantitativo, relatório de inconsistências, versão e atualização.</summary>
    public sealed partial class Comandos
    {
        /// <summary>Quantitativo de blocos em CSV para o Excel (questionário, itens 7, 49 e 54).</summary>
        [CommandMethod("ALVQUANT")]
        public void GerarQuantitativo()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var catalogo = CatalogoLocal.Carregar();
            var cfg = LerConfig(doc);

            var filtro = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
            var sel = ed.GetSelection(
                new PromptSelectionOptions { MessageForAdding = "\nSelecione a planta para o quantitativo (Enter = desenho inteiro): " },
                filtro);
            if (sel.Status == PromptStatus.Cancel) return;
            if (sel.Status != PromptStatus.OK) sel = ed.SelectAll(filtro);
            if (sel.Status != PromptStatus.OK) { ed.WriteMessage("\nNenhum bloco encontrado."); return; }

            var nomes = new List<string>();
            int ignorados = 0;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in sel.Value.GetObjectIds())
                {
                    var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                    var defId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                    var nome = ((BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead)).Name;
                    if (catalogo.BuscarPorBloco(nome) != null) nomes.Add(nome);
                    else ignorados++;
                }
            }
            if (nomes.Count == 0)
            {
                ed.WriteMessage($"\nNenhum bloco do catálogo na seleção ({ignorados} outros blocos ignorados).");
                return;
            }

            var linhas = AlvMod.Core.Saidas.Quantitativo.Contar(nomes, catalogo, cfg.Precos);
            var caminho = Arquivos.AoLadoDoDesenho(doc, "quantitativo", ".csv");
            Arquivos.Salvar(caminho, AlvMod.Core.Saidas.Quantitativo.ParaCsv(linhas, $"Quantitativo de blocos — {Path.GetFileName(doc.Name)}"));

            foreach (var l in linhas)
                ed.WriteMessage($"\n  {l.Bloco}: {l.Quantidade}");
            ed.WriteMessage($"\nTotal: {nomes.Count} blocos ({ignorados} blocos fora do catálogo ignorados).");
            ed.WriteMessage($"\nQuantitativo salvo em {caminho}");
            Arquivos.Abrir(caminho);
        }

        /// <summary>Relatório .docx das inconsistências desta sessão (questionário, item 50).</summary>
        [CommandMethod("ALVRELATORIO")]
        public void Relatorio()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ocorrencias = Registro.Do(doc);
            var caminho = Arquivos.AoLadoDoDesenho(doc, "inconsistencias", ".docx");
            Arquivos.Salvar(caminho, RelatorioDocx.Gerar(
                "Relatório de inconsistências — AlvMod",
                $"Desenho: {Path.GetFileName(doc.Name)} — versão do plugin {Atualizador.VersaoAtualTexto}",
                ocorrencias.ToList()));

            doc.Editor.WriteMessage($"\n{ocorrencias.Count} ocorrência(s). Relatório salvo em {caminho}");
            Arquivos.Abrir(caminho);

            if (ocorrencias.Count > 0 && Perguntas.SimNao(doc.Editor, "\nLimpar a lista de ocorrências?", false) == true)
                ocorrencias.Clear();
        }

        [CommandMethod("ALVVERSAO")]
        public void Versao()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage($"\nAlvMod {Atualizador.VersaoAtualTexto} — repositório {Atualizador.Repositorio}" +
                            (Atualizador.PastaBundle == null ? " (carregado por NETLOAD)" : $"\nInstalado em {Atualizador.PastaBundle}") +
                            $"\nCatálogo em {CatalogoLocal.Caminho}");
        }

        [CommandMethod("ALVATUALIZAR")]
        public void Atualizar()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            ReleaseGitHub release;
            try
            {
                release = Atualizador.BuscarAtualizacao();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nNão foi possível consultar o GitHub: " + ex.Message);
                return;
            }

            if (release == null)
            {
                ed.WriteMessage($"\nAlvMod {Atualizador.VersaoAtualTexto} já é a versão mais recente.");
                return;
            }
            App.PerguntarEAtualizar(release);
        }
    }
}
