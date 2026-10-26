using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace AlvMod.Plugin.Desenho
{
    /// <summary>Layers, estilo de texto e estilo de cota usados nos desenhos do plugin.</summary>
    internal static class Camadas
    {
        public const string EstiloTexto = "Arial";

        /// <summary>Cria o layer se não existir; layer existente mantém a cor do escritório.</summary>
        public static string Garantir(Database db, Transaction tr, string nome, short cor)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(nome)) return nome;
            lt.UpgradeOpen();
            var novo = new LayerTableRecord
            {
                Name = nome,
                Color = Color.FromColorIndex(ColorMethod.ByAci, cor >= 1 && cor <= 255 ? cor : (short)7)
            };
            lt.Add(novo);
            tr.AddNewlyCreatedDBObject(novo, true);
            return nome;
        }

        /// <summary>Estilo de texto "Arial" (o da modulação do engenheiro).</summary>
        public static ObjectId Texto(Database db, Transaction tr)
        {
            var tt = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tt.Has(EstiloTexto)) return tt[EstiloTexto];
            tt.UpgradeOpen();
            var est = new TextStyleTableRecord { Name = EstiloTexto, FileName = "arial.ttf" };
            var id = tt.Add(est);
            tr.AddNewlyCreatedDBObject(est, true);
            return id;
        }

        /// <summary>
        /// Estilo de cota "ALVMOD 1-{escala}": medidas em cm qualquer que seja a unidade do desenho (DIMLFAC =
        /// cm por unidade) e tamanhos em mm no papel (DIMSCALE = escala / (10 × fator)): texto 2 mm, como o do
        /// engenheiro (estilo "1-25"). Atualizado a cada uso, para valer a unidade/escala da obra.
        /// </summary>
        public static ObjectId Cota(Database db, Transaction tr, double fator, double escala)
        {
            string nome = $"ALVMOD 1-{escala:0.##}";
            var dt = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            DimStyleTableRecord est;
            if (dt.Has(nome))
            {
                est = (DimStyleTableRecord)tr.GetObject(dt[nome], OpenMode.ForWrite);
            }
            else
            {
                dt.UpgradeOpen();
                est = new DimStyleTableRecord { Name = nome };
                dt.Add(est);
                tr.AddNewlyCreatedDBObject(est, true);
            }
            est.Dimscale = escala / (10.0 * fator);
            est.Dimlfac = fator;
            est.Dimtxt = 2.0;
            est.Dimasz = 1.0;
            est.Dimexo = 1.0;
            est.Dimexe = 0.5;
            est.Dimgap = 0.8;
            est.Dimdec = 0;
            est.Dimtad = 1;
            est.Dimtih = false;
            est.Dimtoh = false;
            est.Dimtxsty = Texto(db, tr);
            est.Dimclrd = Color.FromColorIndex(ColorMethod.ByAci, 4);
            est.Dimclre = Color.FromColorIndex(ColorMethod.ByAci, 4);
            return est.ObjectId;
        }
    }
}
