# AlvMod — modulação de alvenaria autoportante para AutoCAD

Plugin para AutoCAD 2022–2024 que gera a 1ª e a 2ª fiada de alvenaria autoportante
(família 39, concreto, largura 14).

- Análise do projeto, falhas e perguntas em aberto: [docs/ANALISE.md](docs/ANALISE.md)

---

## Para quem usa o AutoCAD (instalação uma vez só)

1. Feche o AutoCAD.
2. Abra o **PowerShell** (menu Iniciar → digite "PowerShell") e cole:

   ```powershell
   irm https://raw.githubusercontent.com/Daniel-Vilarinho/plugin-modulacao-alvenaria/main/instalar.ps1 | iex
   ```

3. Abra o AutoCAD. A linha de comando mostra `AlvMod x.y.z carregado`.

Não precisa de NETLOAD nem de mexer no SECURELOAD: o plugin fica em
`%APPDATA%\Autodesk\ApplicationPlugins\AlvMod.bundle`, que o AutoCAD carrega sozinho.

### Atualizações

Toda vez que o AutoCAD abre, o plugin verifica se há versão nova. Se houver, aparece a janela
**"Há uma nova versão do AlvMod… Atualizar agora? [Sim] [Não]"**. Clique **Sim**, feche e abra o
AutoCAD. Também dá para verificar na hora com o comando `ALVATUALIZAR`.

### Comandos

| Comando | O que faz |
|---|---|
| `ALV1FIADA` | Pergunta a unidade, pede o eixo da parede (LINE ou segmento de POLYLINE) e desenha a 1ª fiada. Se o comprimento não fecha, avisa `[INCONSISTÊNCIA]` e sugere os comprimentos válidos mais próximos. |
| `ALV2FIADA` | Pede o eixo e as peças da 1ª fiada (já editadas à mão) e desenha a 2ª fiada sem junta a prumo, nos layers com sufixo ` 2F`. |
| `ALVVERSAO` | Mostra a versão instalada. |
| `ALVATUALIZAR` | Procura e instala versão nova. |

**Unidade:** responda quantos cm vale 1 unidade do desenho: `1` se o desenho está em cm, `100` em
metros, `0.1` em milímetros.

**Comprimentos que fecham (família 39):** só os terminados em **4 ou 9** (ex.: 599, 604, 639).

### Limitações da versão atual

- Só paredes retas e isoladas: ainda não trata encontros L/T/X nem janelas.
- Catálogo fixo (família 39). O bloco 54 fica desligado até a fase de encontros.
- As peças são inseridas como blocos DWG (`P-Bloco39`, `P-Bloco 19`…), assumindo o ponto base no
  canto inferior esquerdo e o bloco desenhado em cm. Se o bloco não existir no desenho, o plugin cria
  um bloco provisório (retângulo) com o mesmo nome e avisa.

---

## Para quem desenvolve

```
src/AlvMod.Core        motor de modulação (netstandard2.0, SEM AutoCAD) — testável
src/AlvMod.Plugin      comandos do AutoCAD + atualizador (net48, x64)
tests/AlvMod.Core.Tests  testes do motor (xUnit)
packaging/             PackageContents.xml do bundle
build/empacotar.ps1    compila e gera out/AlvMod-vX.Y.Z.zip
instalar.ps1           instalador para a máquina do engenheiro
```

As referências do AutoCAD vêm do pacote NuGet oficial `AutoCAD.NET` (24.1 = AutoCAD 2022), com
`ExcludeAssets=runtime`, que equivale a "Copiar Local = Falso". Por isso dá para compilar sem o
AutoCAD instalado.

### Testar o motor

```bash
dotnet test tests/AlvMod.Core.Tests
```

### Publicar uma versão (deploy)

```bash
git tag v0.2.0
git push origin v0.2.0
```

O workflow `Release` testa, compila, empacota e publica a release no GitHub. Na próxima vez que o
engenheiro abrir o AutoCAD, aparece a pergunta para atualizar.
