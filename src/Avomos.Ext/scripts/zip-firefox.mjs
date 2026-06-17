import { execSync } from 'child_process';
execSync('cd dist-firefox && tar -a -cf ../avomos-firefox.xpi *', { stdio: 'inherit', shell: true });
