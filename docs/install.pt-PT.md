# Instalar o Candlelight noutro PC

**[Descarregar o instalador](https://github.com/DacostaWeb/Candlelight/releases/latest/download/Candlelight-Setup-win-x64.exe)** · [Todas as versões](https://github.com/DacostaWeb/Candlelight/releases)

## Instalação

1. Executa `Candlelight-Setup-win-x64.exe`. Suporta Windows 10 (build 19041+) e Windows 11 em PCs compatíveis com x64 e inclui o runtime .NET.
2. Mantém **Iniciar com o Windows** selecionado. O atalho no ambiente de trabalho é opcional.
3. No fim, mantém **Configurar a Luz noturna do Windows** selecionado. A página abre nas Definições do Windows.
4. Liga **Agendar luz noturna** e escolhe **Do pôr ao nascer do sol**. Ajusta a intensidade ao teu gosto; no PC de referência foi usada a intensidade máxima.
5. No Candlelight, indica a tua cidade em **Localização do sol** e ajusta os valores de cada ecrã.

O instalador guarda a aplicação apenas no teu utilizador, sem serviço adicional. Pode ser usado offline depois de descarregado. A instalação não altera permissões de localização ou outros ajustes de privacidade do Windows. O instalador não tem assinatura digital, pelo que o Windows pode mostrar um aviso de editor desconhecido; as releases incluem `SHA256SUMS.txt` para verificar os downloads.

## Configuração por monitor

| Ecrã | Noite | Dia |
| --- | --- | --- |
| Interno | 500 K / 100% | 2700 K / 100% |
| OLED | 500 K / 25% | 2700 K / 100% |

Os novos perfis usam 500 K de noite e 2700 K de dia, com brilho a 100%; define manualmente o brilho noturno do OLED. A transição da manhã começa ao nascer do sol e dura duas horas. A da tarde começa duas horas antes do pôr do sol e termina exatamente nessa hora. Podes alterar as curvas e a duração para cada monitor.

O botão **Cores normais** suspende os ajustes do Candlelight nesse monitor. Se a Luz noturna do Windows estiver ligada, pode continuar a aquecer as cores: desliga-a temporariamente no Windows quando precisares de fidelidade de cor.

## Luz noturna e privacidade

O Candlelight calcula os seus horários solares a partir da cidade escolhida. A Luz noturna tem a sua própria localização, gerida pelo Windows. Ativa **Definições → Privacidade e segurança → Localização → Serviços de localização** para que o Windows possa calcular os horários. Também podes definir uma localização predefinida nessa página.

A localização do Windows pode enviar dados de localização e de redes próximas à Microsoft; as permissões das aplicações são geridas separadamente. Consulta a [documentação de privacidade da Microsoft](https://support.microsoft.com/en-us/windows/privacy/windows-location-service-and-privacy).

Se a opção solar não aparecer ou mostrar **00:00–00:00**, depois de usar WinUtil/Chris Titus ou outra ferramenta de privacidade:

1. Confirma que os serviços de localização estão ligados nas Definições.
2. No WinUtil, desfaz apenas **Location Tracking – Disable**. O serviço de geolocalização (`lfsvc`) deve poder arrancar, com o tipo de arranque **Manual**.
3. Algumas versões antigas também definiam `Status=0` em `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\lfsvc\Service\Configuration`. O valor ativado é `1`. Esta correção específica requer administrador; não é aplicada silenciosamente pelo instalador. [Código antigo do WinUtil](https://github.com/ChrisTitusTech/winutil/issues/1920).
4. Guarda o trabalho e **reinicia** o PC. Confirma que o horário do Windows já mostra horas reais e mantém **Do pôr ao nascer do sol** selecionado.

No PC testado, a correção da localização e o reinício permitiram ao Windows recalcular os horários. Com os ajustes extra do Candlelight desligados, o utilizador confirmou que o clarão inicial passou a quente/amarelo. Outros drivers e monitores podem comportar-se de forma diferente.

## Atualizar, remover ou usar sem instalar

Desde a **0.6.3**, o rodapé mostra a versão instalada e o botão **Procurar
atualizações**. Com **Procurar automaticamente** ligado, verifica ao abrir
(mesmo na bandeja) e de três em três horas. O botão passa a **Instalar [versão]**
quando há uma atualização. Só depois desse clique descarrega, verifica, instala
e reabre a aplicação, preservando os teus perfis e a preferência de arranque.

Instala a 0.6.3 uma vez em cada PC para obter estes controlos. A opção automática
vem ligada em instalações novas e mantém a preferência existente em atualizações.
Na versão portátil, o botão abre os downloads para atualização manual.

A versão 0.6.2 corrige tentativas contínuas após uma falha do Windows ao aplicar
a cor. Se aparecer **Ajustes suspensos para evitar flashes**, o Candlelight
deixa de escrever nesse monitor. Os restantes mantêm os seus ajustes. Quando
o ecrã estiver estável, **Opções de retoma → Reaplicar agora** faz uma nova
tentativa. Se voltar a falhar, fica suspenso novamente. Alterar curvas ou acordar
o PC não reinicia esse ciclo; os perfis guardados são preservados.

Instala a versão nova por cima da anterior: os monitores, curvas, horários e presets são preservados. Na primeira atualização para 0.6.1, os dois ajustes experimentais de retoma ficam desligados. Mantêm-se em **Opções de retoma** para quem os quiser ativar explicitamente.

Os dados ficam em `%APPDATA%\Candlelight\Settings.json`. A desinstalação em **Definições → Aplicações → Candlelight** mantém esses dados. A versão ZIP guarda as definições junto de `Candlelight.exe`; fecha outras cópias antes de a executar.

Cada PC deve configurar os seus próprios monitores. Os downloads públicos não incluem configurações pessoais, identificadores de monitores ou localização do utilizador.
