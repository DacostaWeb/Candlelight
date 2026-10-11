# Horários no Candlelight 0.4

![Gráfico diário](next-interface.png)

Escolhe o monitor no topo. As horas são partilhadas por defeito; a temperatura,
o brilho e o vermelho puro pertencem apenas ao monitor selecionado.

## Programa e Só hoje

**Programa** edita o horário que se repete diariamente. **Só hoje** permite mexer
nos mesmos pontos, curvas, cores e brilho sem substituir esse programa. As
alterações aplicam-se ao largar os controlos; não precisas de guardar.
À **meia-noite no fuso da cidade**, volta automaticamente ao programa do novo dia.
Também acontece com a janela fechada, após suspensão ou reinício. **Retomar
programa** descarta antecipadamente todos os ajustes temporários.

A aplicação abre em **Só hoje**. Para preparar a rotina anual, escolhe
**Programa → Ciclo solar…**. Se estiver ativo um ajuste temporário, uma mensagem
avisa que continua a ser usado até à meia-noite enquanto editas o programa.

## Preparar o ciclo solar

Em **Ciclo solar…**, configura **Nascer do sol** e **Pôr do sol**:

1. Define quantos minutos antes (negativo) ou depois (positivo) começa a mudança.
2. Define a duração dessa transição.
3. Arrasta os dois controlos da curva, usa os valores exatos ou escolhe uma forma
   inicial: Suave, Linear, Começa devagar ou Termina devagar.

O assistente mostra as horas de início e fim para a data em prévia. Ao aplicar,
substitui os pontos do horário por estas duas transições; cada ecrã começa com
as suas próprias cores mais fria e mais quente. Ajusta as cores no editor
principal e acrescenta pontos se precisares de mais fases. Pontos acrescentados
a um ciclo inteiramente solar também ficam ligados ao sol mais próximo.

O cálculo acompanha cada dia do ano, incluindo a hora de verão. Clica na data
do gráfico para saltar para o verão/inverno e prever o resultado, sem mudar o
filtro atual. Ao ver outro dia, o editor passa para **Programa**.

![Editor da curva](solar-curve.png)

**Curva…**, ou um duplo clique num ponto ou numa transição, abre o editor
detalhado. A duração e a curva desse ponto podem usar o valor global ou ter
valores próprios. As horas, durações e formas da curva são partilhadas quando
**Horários partilhados** está marcado; a cor e o brilho continuam individuais.
Cancelar/Escape fecha o diálogo sem aplicar o rascunho. Dentro do gráfico da
curva, Escape cancela o arrasto; Espaço escolhe a outra pega e as setas ajustam.

- Arrasta um ponto na horizontal para mudar a hora e na vertical para ajustar
  a temperatura ou o brilho. Usa os separadores do gráfico para escolher o valor.
  As mudanças são guardadas ao largar; Escape cancela o arrasto.
- Um duplo clique numa zona livre do gráfico, ou **+ Ponto**, acrescenta uma mudança. **−** ou
  Delete remove o ponto selecionado. **Desfazer** e Ctrl+Z recuperam a última alteração.
- Seleciona um ponto para editar os valores exatos abaixo. As setas no gráfico
  movem a hora em passos de cinco minutos (Shift: um minuto) e o valor em passos
  de 50 K ou 1%. Vermelho puro conserva o seu modo durante o arrasto.
- **Por defeito** define a duração das mudanças sem duração própria, em todos os
  monitores, incluindo os que tenham horários independentes. Zero é imediato;
  a opção **Suaves** usa uma curva gradual e **Lineares** uma variação uniforme.
- As horas indicam o início da mudança. Se outra mudança chegar antes de a
  transição terminar, a duração encurta até esse ponto. Uma curva personalizada
  pode entrar e sair gradualmente do vermelho puro: durante a mudança a
  temperatura aproxima-se de 500 K; no destino, o modo vermelho tem verde e
  azul exatamente a zero. Os horários antigos mantêm a passagem imediata.
- **Copiar cores…** copia os valores de outro ecrã. Desmarca **Horários
  partilhados** para começar com as mesmas horas e passar a editá-las separadamente.
  Voltar a ligar preserva valores próprios, avaliados nas horas comuns.
- **Ajustar agora** começa no filtro atual e passa este monitor a manual só hoje.
  **Horário ativo**, ou **Retomar horário** no tabuleiro, reativa a programação.
  Editar um ponto guardado não desativa o horário.

## Seguir o sol

Escolhe uma cidade no rodapé. A pesquisa por nome usa Open-Meteo/GeoNames; também
podes preencher coordenadas e fuso horário. Os cálculos seguintes funcionam sem
internet. Póvoa de Varzim usa aproximadamente 41.38215, −8.75776, Europe/Lisbon.

No ponto desejado, troca **Hora fixa** por **Nascer do sol** ou **Pôr do sol**.
O campo de minutos aceita valores negativos para antecipar e positivos para
atrasar. Estas horas são recalculadas para a data e acompanham a hora de verão.
As setas junto à data mostram outros dias sem avançar o relógio nem aplicar o
filtro desse dia. Editar nessa vista continua a alterar o horário guardado.

Os marcadores solares aparecem mesmo quando os pontos ainda usam horas fixas.
A migração preserva as horas existentes; escolher a cidade não converte esses
pontos automaticamente. Em regiões sem nascer/pôr do sol numa data, o ponto usa
a sua hora fixa de reserva, e a interface indica a falta do evento.

## Dados e verificação

O esquema 3 mantém o programa e o ajuste temporário em campos separados. Este
último inclui o instante de expiração e é guardado apenas para sobreviver a
reinícios. A avaliação ignora-o a partir da meia-noite, antes mesmo de o ficheiro
ser limpo. O ficheiro original dos esquemas 1 ou 2 é copiado para `.bak` antes
da migração. Os pontos e cores existentes não são convertidos para solares
automaticamente.

O esquema 2 já separava pontos de tempo (com identificador estável) dos valores de
cada monitor. A migração escolhe o horário existente mais completo e ativo como
base, preenche ecrãs sem pontos e preserva horários distintos como independentes.
A duração mais frequente desse horário torna-se global. O ficheiro anterior é
guardado antes da primeira escrita. As definições continuam em
`%LOCALAPPDATA%\Candlelight.Next\Settings.json`.

O controlador e o gráfico usam a mesma avaliação por instantes, incluindo dias
adjacentes para transições à meia-noite, offsets solares e mudanças da hora.
Horas inexistentes no avanço da primavera passam ao primeiro minuto válido;
a hora repetida do outono executa na primeira ocorrência. Uma hora fixa explícita
tem prioridade se coincidir exatamente com um evento solar.

O módulo protegido recebe apenas eventos de cor datados, curvas limitadas e a
expiração dos ajustes, validados num pacote
limitado a 64 KiB. Mantém compatibilidade de leitura com os horários da 0.2.9;
não recebe cidade, acesso à rede, caminhos de ficheiros ou comandos. O pacote
inclui o programa original para poder retomá-lo à meia-noite sem depender de
uma nova mensagem do processo normal. Atualiza a leitura enquanto está ativo.

Verificado: testes unitários de migração, isolamento entre monitores, ligação e
separação, transições, estações, DST, offsets a atravessar a meia-noite, regiões
polares e equivalência da curva no módulo protegido. O ensaio da interface usa
definições isoladas e nenhum renderer real: arrasto com gravação no fim, Escape,
teclado, desfazer, valores numéricos, localização/fuso, modos manuais e ausência
de controlos cortados em janela compacta a 150% DPI. A versão 0.4 acrescenta
testes dos 365 dias do ano, formas Bézier, pontos solares adicionais, expiração
exata à meia-noite em dias de 23/25 horas, reinício e retorno independente do
módulo protegido ao programa. Os diálogos exercitam arrasto, cancelamento,
valores precisos e a separação entre ajustes temporários e o programa.

Referências: [f.lux](https://justgetflux.com/news/pages/v4/welcome/),
[equações solares NOAA](https://www.gml.noaa.gov/grad/solcalc/solareqns.PDF),
[API de pesquisa](https://open-meteo.com/en/docs/geocoding-api),
[coordenadas GeoNames](https://www.geonames.org/8014137/p-voa-de-varzim.html).
