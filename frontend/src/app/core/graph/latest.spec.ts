import { Latest } from './latest';

describe('Latest', () => {
  it('ignora a resposta antiga que chega depois da nova', async () => {
    const latest = new Latest();
    let resolveOld!: (value: string) => void;
    const old = new Promise<string>((resolve) => (resolveOld = resolve));
    const applied: string[] = [];

    const first = latest.run(old, (value) => applied.push(value));
    const second = latest.run(Promise.resolve('novo'), (value) => applied.push(value));
    await second;
    resolveOld('antigo');
    await first;

    expect(applied).toEqual(['novo']);
  });
});
