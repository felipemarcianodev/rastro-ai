import { TestBed } from '@angular/core/testing';
import { LastRepositoryService } from './last-repository.service';

describe('LastRepositoryService', () => {
  beforeEach(() => localStorage.removeItem('rastro.lastRepository'));
  afterEach(() => localStorage.removeItem('rastro.lastRepository'));

  // WEB-UXC-001
  it('lembra o último repositório e o lê de novo numa visita nova', () => {
    const first = TestBed.inject(LastRepositoryService);
    expect(first.name()).toBeNull();

    first.remember('loja');
    expect(first.name()).toBe('loja');

    TestBed.resetTestingModule();
    expect(TestBed.inject(LastRepositoryService).name()).toBe('loja');
  });
});
