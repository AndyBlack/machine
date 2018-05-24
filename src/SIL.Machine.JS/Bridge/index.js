require('./lib/bridge');
require('./lib/newtonsoft.json');
require('./lib/machine');

var machine = {};

function addClass(ns, cls)
{
    machine[cls] = ns[cls];
}

addClass(SIL.Machine.Translation, 'TrainResultCode');
addClass(SIL.Machine.Translation, 'TranslationEngine');
addClass(SIL.Machine.Translation, 'InteractiveTranslationSession');
addClass(SIL.Machine.Translation, 'ProgressStatus');
addClass(SIL.Machine.Tokenization, 'SegmentTokenizer');

module.exports = machine;